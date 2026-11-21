using System;
using System.Collections.Generic;
using BigBlueFish.Windows;

internal static class Program
{
    private static int checks;
    private static readonly CompanionRect Work = new(0, 0, 1920, 1040);
    private static readonly CompanionRect Body = new(1000, Work.Bottom - 216, 171, 216);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static CompanionSurface Surface(nint handle, double x, double y, double w, double h,
        bool foreground = false, bool occluded = false, int order = 0, string title = "窗口") =>
        new(handle, new CompanionRect(x, y, w, h), title, 100, order, foreground, occluded);

    private static List<CompanionSurface> Surfaces() =>
    [
        Surface(1, 200, 300, 800, 500),            // perch target
        Surface(2, 1200, 600, 600, 440),           // reaches the floor, push target
        Surface(3, 100, 20, 700, 400),             // no room above
        Surface(4, 50, 400, 100, 300),             // too narrow
        Surface(5, 300, 200, 600, 500, occluded: true),
    ];

    private static CompanionRoaming Roaming(List<CompanionSurface> surfaces, int seed = 7,
        CompanionRoamingOptions? options = null)
    {
        var roam = new CompanionRoaming(new Random(seed), options ?? new CompanionRoamingOptions());
        roam.Sync(Body);
        roam.ResetSchedule(DateTime.Now);
        _ = surfaces;
        return roam;
    }

    private static bool Inside(CompanionRect body, CompanionRect work) =>
        body.Left >= work.Left - .5 && body.Right <= work.Right + .5
        && body.Top >= work.Top - .5 && body.Bottom <= work.Bottom + .5;

    private static void Run(CompanionRoaming roam, ref DateTime now, List<CompanionSurface> surfaces,
        Action<CompanionRoaming>? observe = null, int guard = 400, bool expectFinish = true)
    {
        for (int i = 0; i < guard; i++)
        {
            roam.Tick(now, Work, surfaces, true, 30);
            observe?.Invoke(roam);
            if (roam.State == CompanionRoamState.Idle && !roam.IsRoaming && !roam.HasPendingAction)
            {
                Check(expectFinish, "trip finished unexpectedly");
                return;
            }
            var action = roam.TakeAction();
            if (action == null)
            {
                now = now.AddMilliseconds(100);
                continue;
            }
            double end = action.EndSeconds ?? 10;
            for (double t = 0; t <= end; t += .1)
            {
                roam.Advance(t);
                Check(Inside(roam.Body, Work), "body left the work area");
            }
            roam.Advance(end);
            roam.ClipFinished(now, action.Token);
            now = now.AddSeconds(10);
        }
        if (expectFinish) throw new Exception("trip did not finish");
    }

    private static void Until(CompanionRoaming roam, ref DateTime now, List<CompanionSurface> surfaces,
        Func<CompanionRoaming, bool> condition, int guard = 120)
    {
        for (int i = 0; i < guard; i++)
        {
            roam.Tick(now, Work, surfaces, true, 30);
            if (condition(roam)) return;
            var action = roam.TakeAction();
            if (action == null) { now = now.AddMilliseconds(100); continue; }
            double end = action.EndSeconds ?? 10;
            for (double t = 0; t <= end; t += .1) roam.Advance(t);
            roam.Advance(end);
            roam.ClipFinished(now, action.Token);
            now = now.AddSeconds(10);
        }
        throw new Exception("condition not reached");
    }

    private static int Main()
    {
        var now = new DateTime(2026, 11, 19, 15, 0, 0);
        var surfaces = Surfaces();

        // 1. Geometry and clamping.
        var rect = CompanionRect.FromLTRB(10, 20, 30, 60);
        Check(Math.Abs(rect.Width - 20) < .001 && Math.Abs(rect.Height - 40) < .001, "rect from ltrb");
        Check(rect.Contains(20, 40) && !rect.Contains(31, 40), "rect contains");
        var roam = Roaming(surfaces);
        Check(roam.Body == Body, "sync keeps the supplied body");

        // 2. Filtering: occluded, narrow, no-room-above and foreground push targets.
        Check(CompanionSurfacePolicy.ChoosePerch([surfaces[2], surfaces[3], surfaces[4]], Work, 171, 216, 1000, new Random(1)) == null,
            "perch filters occluded, narrow and top-edge surfaces");
        Check(CompanionSurfacePolicy.ChoosePush([Surface(9, 100, 600, 800, 440, foreground: true)], Work, 171, 1000, new Random(1)) == null,
            "foreground windows are never pushed");
        Check(CompanionSurfacePolicy.ChooseSide(Surface(10, 0, 300, 1920, 500), Work, 171, 960) == null,
            "full-width windows have no climbable side");

        // 3. Stroll stays on the floor and returns to idle.
        roam = Roaming(surfaces);
        Check(roam.TryStartStroll(now, Work, surfaces), "stroll starts");
        Check(roam.IsRoaming && roam.State == CompanionRoamState.Walk, "stroll enters walk");
        Check(roam.TakeAction() is { } firstWalk && firstWalk.Id == CompanionRoamActions.Walk, "stroll requests the existing walk clip");
        roam.Cancel();
        roam = Roaming(surfaces);
        roam.TryStartStroll(now, Work, surfaces);
        bool sawStroll = false;
        Run(roam, ref now, surfaces, r =>
        {
            if (r.State == CompanionRoamState.Walk) sawStroll = true;
            Check(Math.Abs(r.Body.Bottom - Work.Bottom) < .6, "stroll keeps feet on the floor");
        });
        Check(sawStroll, "stroll observed");
        Check(roam.State == CompanionRoamState.Idle && !roam.IsRoaming, "stroll ends idle");
        Check(roam.NextRoamAt > now, "next roam scheduled");

        // 4. Perch: approach, climb, top walk, sit on the real window edge.
        now = new DateTime(2026, 11, 19, 15, 0, 0);
        roam = Roaming(surfaces);
        Check(roam.TryStartPerch(now, Work, surfaces), "perch starts");
        bool sawClimb = false, sawPerch = false, perched = false;
        Run(roam, ref now, surfaces, r =>
        {
            if (r.State == CompanionRoamState.Climb) sawClimb = true;
            if (r.State != CompanionRoamState.Perch) return;
            sawPerch = true;
            var target = r.Target ?? throw new Exception("perched without target");
            Check(Math.Abs(r.Body.Bottom - (target.Bounds.Top + 2)) < .6, "perch feet sit on the window top edge");
            Check(r.Body.CenterX >= target.Bounds.Left && r.Body.CenterX <= target.Bounds.Right, "perch stays on the window width");
            perched = true;
        });
        Check(sawClimb && sawPerch && perched, "climb and perch observed");
        Check(roam.State == CompanionRoamState.Idle, "perch trip returns to idle");

        // 5. Following a moving window, then falling when it disappears.
        now = new DateTime(2026, 11, 19, 15, 0, 0);
        roam = Roaming(surfaces);
        roam.TryStartPerch(now, Work, surfaces);
        Until(roam, ref now, surfaces, r => r.State == CompanionRoamState.Perch);
        var before = roam.Body;
        var perchTarget = roam.Target ?? throw new Exception("perch target missing");
        var moved = Surfaces();
        int index = moved.FindIndex(surface => surface.Handle == perchTarget.Handle);
        Check(index >= 0, "perch target is one of the enumerated windows");
        var oldBounds = moved[index].Bounds;
        moved[index] = moved[index] with { Bounds = oldBounds.Offset(60, -30) };
        roam.Tick(now.AddMilliseconds(40), Work, moved, true, 30);
        Check(Math.Abs(roam.Body.Left - (before.Left + 60)) < .6, "perch follows horizontal window moves");
        Check(Math.Abs(roam.Body.Top - (before.Top - 30)) < .6, "perch follows vertical window moves");
        Check(roam.Target?.Bounds == moved[index].Bounds, "target snapshot refreshed");

        moved.RemoveAt(index);
        roam.Tick(now.AddMilliseconds(80), Work, moved, true, 30);
        Check(roam.State == CompanionRoamState.Perch, "missing window has a grace period");
        roam.Tick(now.AddSeconds(2), Work, moved, true, 30);
        Check(roam.State == CompanionRoamState.Fall, "missing window drops the pet");
        var fall = roam.TakeAction() ?? throw new Exception("fall action queued");
        Check(fall.Id == CompanionRoamActions.Fall, "fall reuses the suspended drag clip");
        for (double t = 0; t <= (fall.EndSeconds ?? 2) + .1; t += .1) roam.Advance(t);
        roam.ClipFinished(now.AddSeconds(3), fall.Token);
        Check(roam.State == CompanionRoamState.Idle, "fall lands idle");

        // 6. Cosmetic push: knock, get angry, never touch the target window.
        now = new DateTime(2026, 11, 19, 15, 0, 0);
        roam = Roaming(surfaces);
        Check(roam.TryStartPush(now, Work, surfaces), "ground push starts");
        bool sawPush = false, sawAngry = false;
        Run(roam, ref now, surfaces, r =>
        {
            sawPush |= r.State == CompanionRoamState.Push;
            sawAngry |= r.PendingActionId == CompanionRoamActions.PushFail;
        });
        Check(sawPush, "push state observed");
        Check(sawAngry, "push fails and gets angry without moving the window");
        Check(roam.State == CompanionRoamState.Idle, "push trip ends idle");

        // 7. Scale invariance: the same geometry at 200% in physical units.
        var work2 = new CompanionRect(0, 0, 3840, 2080);
        var body2 = new CompanionRect(2000, work2.Bottom - 432, 342, 432);
        var surfaces2 = new List<CompanionSurface> { Surface(20, 400, 600, 1600, 1000) };
        var roam2 = new CompanionRoaming(new Random(3), new CompanionRoamingOptions());
        roam2.Sync(body2);
        var now2 = new DateTime(2026, 11, 19, 15, 0, 0);
        Check(roam2.TryStartPerch(now2, work2, surfaces2), "scaled perch starts");
        for (int i = 0; i < 200 && roam2.State != CompanionRoamState.Perch; i++)
        {
            roam2.Tick(now2, work2, surfaces2, true, 30);
            var action = roam2.TakeAction();
            if (action == null) { now2 = now2.AddMilliseconds(100); continue; }
            double end = action.EndSeconds ?? 10;
            for (double t = 0; t <= end; t += .1) roam2.Advance(t);
            roam2.Advance(end);
            roam2.ClipFinished(now2, action.Token);
            now2 = now2.AddSeconds(10);
        }
        Check(roam2.State == CompanionRoamState.Perch, "scaled perch reached");
        Check(Math.Abs(roam2.Body.Bottom - (surfaces2[0].Bounds.Top + 2)) < .6, "scaled perch alignment");
        Check(roam2.Body.Right <= work2.Right + .5 && roam2.Body.Left >= work2.Left - .5, "scaled body stays on screen");

        // 8. Scheduling, permission changes and cancellation.
        var options = new CompanionRoamingOptions
        {
            FirstDelayMin = TimeSpan.Zero, FirstDelayMax = TimeSpan.Zero,
            MinIdleSeconds = 0, PerchChance = 0, PushChance = 0,
        };
        var roam3 = new CompanionRoaming(new Random(11), options);
        roam3.Sync(Body);
        roam3.ResetSchedule(now);
        roam3.Tick(now, Work, surfaces, true, 0);
        Check(roam3.IsRoaming, "scheduled roam starts when idle and allowed");
        roam3.Tick(now.AddMilliseconds(40), Work, surfaces, false, 0);
        Check(!roam3.IsRoaming && roam3.State == CompanionRoamState.Idle && !roam3.HasPendingAction,
            "hiding or settings cancels the trip");

        var roam4 = Roaming(surfaces);
        roam4.TryStartPerch(now, Work, surfaces);
        roam4.Cancel();
        Check(!roam4.IsRoaming && !roam4.HasPendingAction, "cancel clears pending work");

        // 9. A pet left in mid-air floats down before she starts walking.
        now = new DateTime(2026, 11, 19, 15, 0, 0);
        var high = new CompanionRect(1000, Work.Bottom - 216 - 300, 171, 216);
        var roam5 = new CompanionRoaming(new Random(5), new CompanionRoamingOptions());
        roam5.Sync(high);
        Check(roam5.TryStartStroll(now, Work, surfaces), "mid-air trip is accepted");
        var settle = roam5.TakeAction() ?? throw new Exception("mid-air trip queues a fall");
        Check(settle.Id == CompanionRoamActions.Fall, "mid-air trip floats down first");
        for (double t = 0; t <= (settle.EndSeconds ?? 2) + .1; t += .1) roam5.Advance(t);
        roam5.ClipFinished(now, settle.Token);
        Check(Math.Abs(roam5.Body.Bottom - Work.Bottom) < .6, "settling lands on the floor");
        var resumed = roam5.TakeAction() ?? throw new Exception("the requested trip resumes after settling");
        Check(resumed.Id == CompanionRoamActions.Walk, "the trip resumes with a walk once grounded");
        Check(roam5.IsRoaming && roam5.Trip == CompanionRoamTrip.Stroll, "the resumed trip keeps its kind");
        Check(!roam5.HasPendingAction, "the resumed trip queues exactly one leg at a time");
        for (double t = 0; t <= (resumed.EndSeconds ?? 10) + .1; t += .1) roam5.Advance(t);
        roam5.ClipFinished(now, resumed.Token);
        Check(Math.Abs(roam5.Body.Bottom - Work.Bottom) < .6, "the resumed stroll stays on the floor");
        roam5.Cancel();

        // 10. Turning roaming off mid-perch can drop the pet straight to the floor.
        var roam6 = new CompanionRoaming(new Random(6), new CompanionRoamingOptions());
        roam6.Sync(high);
        roam6.ResetSchedule(now.AddMinutes(5));
        roam6.Tick(now, Work, surfaces, true, 0);
        Check(roam6.MoveToGround(), "a mid-air body can be placed back on the floor");
        Check(Math.Abs(roam6.Body.Bottom - Work.Bottom) < .6, "the forced landing reaches the work-area floor");
        Check(!roam6.IsRoaming && !roam6.HasPendingAction, "the forced landing leaves no trip behind");
        Check(!roam6.MoveToGround(), "a grounded body needs no forced landing");

        Console.WriteLine($"{checks} roaming checks passed.");
        return 0;
    }
}
