using System;
using System.Collections.Generic;
using System.Linq;

namespace BigBlueFish.Windows;

internal enum CompanionRoamState
{
    Idle,
    Walk,
    Climb,
    Perch,
    Push,
    Fall
}

internal enum CompanionRoamTrip
{
    None,
    Stroll,
    Perch,
    Push
}

// Reuses existing clips by id. No animation asset or weight changes are needed.
internal static class CompanionRoamActions
{
    internal const string Walk = "原地左转奔跑";
    internal const string Float = "原地漂浮踏步";
    internal const string Idle = "待机呼吸休闲";
    internal const string Hum = "悠闲哼歌";
    internal const string Nap = "原地小憩沉眠";
    internal const string Fall = "被鼠标拖拽悬空反馈";
    internal const string Push = "原地敲击桌面互动";
    internal const string PushFail = "玩游戏气急败坏";
}

internal sealed record CompanionRoamingAction(long Token, string Id, bool Mirror = false, double PlaybackRate = 1, double? EndSeconds = null);

internal sealed class CompanionRoamingOptions
{
    internal TimeSpan FirstDelayMin { get; set; } = TimeSpan.FromSeconds(45);
    internal TimeSpan FirstDelayMax { get; set; } = TimeSpan.FromMinutes(2);
    internal TimeSpan DelayMin { get; set; } = TimeSpan.FromMinutes(2);
    internal TimeSpan DelayMax { get; set; } = TimeSpan.FromMinutes(6);
    internal TimeSpan PerchMin { get; set; } = TimeSpan.FromSeconds(20);
    internal TimeSpan PerchMax { get; set; } = TimeSpan.FromSeconds(60);
    internal double MinIdleSeconds { get; set; } = 5;
    internal double PerchOverlap { get; set; } = 2;
    internal double MaxWalkFactor { get; set; } = 2.6;
    internal double MaxClimbFactor { get; set; } = 2.8;
    internal double ClimbUnitsPerSecond { get; set; } = 150;
    internal double PerchChance { get; set; } = .45;
    internal double PushChance { get; set; } = .15;
    internal double PerchKnockChance { get; set; } = .35;
    internal bool PerchEnabled { get; set; } = true;
    internal bool PushEnabled { get; set; } = true;
}

// Plans and drives short trips: ground strolls, climbing onto a real window and
// sitting on its top edge, and a purely cosmetic push at a window. The engine owns
// the visible-body rectangle while a trip is running; the platform converts that
// rectangle to a window position. All inputs and outputs use the platform's screen
// units (DIPs on Windows, pixels on Linux).
internal sealed class CompanionRoaming
{
    private enum LegPurpose { None, Stroll, Approach, Climb, TopWalk, PerchIdle, Knock, Angry, Fall }

    private readonly Random random;
    private readonly CompanionRoamingOptions options;
    private CompanionRect body = new(0, 0, 0, 0);
    private CompanionRect lastWork = new(0, 0, 0, 0);
    private IReadOnlyList<CompanionSurface> lastSurfaces = Array.Empty<CompanionSurface>();
    private DateTime nextRoamAt = DateTime.MinValue;
    private CompanionRoamState state = CompanionRoamState.Idle;
    private CompanionRoamTrip trip = CompanionRoamTrip.None;
    private LegPurpose purpose = LegPurpose.None;
    private CompanionSurface? target;
    private CompanionRoamingAction? pending;
    private readonly Queue<CompanionPoint> path = new();
    private readonly Queue<CompanionPoint> climbPath = new();
    private CompanionPoint legFrom;
    private CompanionPoint legTo;
    private double legLead;
    private double legTail;
    private double legDuration = 10;
    private bool legMirror;
    private double climbX;
    private double perchOffsetX;
    private DateTime perchUntil;
    private DateTime targetMissingSince = DateTime.MinValue;
    private DateTime occludedSince = DateTime.MinValue;
    private bool settlePending;
    private CompanionRoamTrip settleResume = CompanionRoamTrip.None;
    private nint? settleHandle;
    private long nextToken;
    private long activeToken;

    internal CompanionRoaming(Random? random = null, CompanionRoamingOptions? options = null)
    {
        this.random = random ?? new Random();
        this.options = options ?? new CompanionRoamingOptions();
    }

    internal CompanionRoamingOptions Options => options;
    internal CompanionRoamState State => state;
    internal CompanionRoamTrip Trip => trip;
    internal CompanionRect Body => body;
    internal CompanionSurface? Target => target;
    internal DateTime NextRoamAt => nextRoamAt;
    internal DateTime PerchUntil => perchUntil;
    internal bool IsRoaming => trip != CompanionRoamTrip.None;
    internal bool HasPendingAction => pending != null;
    internal string? PendingActionId => pending?.Id;
    internal CompanionPoint Feet => new(body.CenterX, body.Bottom);

    internal CompanionRoamingAction? TakeAction()
    {
        if (pending == null) return null;
        var action = pending;
        pending = null;
        activeToken = action.Token;
        return action;
    }

    private void Request(string id, bool mirror = false, double playbackRate = 1, double? endSeconds = null)
        => pending = new(++nextToken, id, mirror, playbackRate, endSeconds);

    // Keeps the engine aligned with what the window actually shows. Called after the
    // window is clamped, scaled, repositioned by a drag, or shown again.
    internal void Sync(CompanionRect visibleBody)
    {
        if (visibleBody.IsEmpty) return;
        bool resized = !body.IsEmpty
            && (Math.Abs(body.Width - visibleBody.Width) > .6 || Math.Abs(body.Height - visibleBody.Height) > .6);
        body = visibleBody;
        if (resized && state != CompanionRoamState.Idle) Cancel();
    }

    // Puts the visible body back on the floor at once. Used when roaming is turned
    // off mid-perch, so the pet cannot be left floating with no trip left to land.
    internal bool MoveToGround()
    {
        if (body.IsEmpty || lastWork.IsEmpty || Feet.Y >= lastWork.Bottom - 2) return false;
        body = PlaceFeet(new(Feet.X, lastWork.Bottom));
        return true;
    }

    internal void ResetSchedule(DateTime now)
    {
        nextRoamAt = now + Span(options.FirstDelayMin, options.FirstDelayMax);
    }

    internal void Cancel()
    {
        state = CompanionRoamState.Idle;
        trip = CompanionRoamTrip.None;
        purpose = LegPurpose.None;
        target = null;
        pending = null;
        path.Clear();
        climbPath.Clear();
        targetMissingSince = DateTime.MinValue;
        occludedSince = DateTime.MinValue;
        settlePending = false;
        settleResume = CompanionRoamTrip.None;
        settleHandle = null;
        activeToken = 0;
    }

    internal void Tick(DateTime now, CompanionRect workArea, IReadOnlyList<CompanionSurface> surfaces, bool allowed, double idleSeconds)
    {
        if (body.IsEmpty) return;
        lastWork = workArea;
        lastSurfaces = surfaces;

        if (!allowed)
        {
            if (state != CompanionRoamState.Idle) Cancel();
            return;
        }

        if (state == CompanionRoamState.Idle)
        {
            if (nextRoamAt == DateTime.MinValue)
                nextRoamAt = now + Span(options.FirstDelayMin, options.FirstDelayMax);
            if (now < nextRoamAt || idleSeconds < options.MinIdleSeconds) return;
            nextRoamAt = now + Span(options.DelayMin, options.DelayMax);
            if (!TryStartTrip(now, workArea, surfaces)) TryStartStroll(now, workArea, surfaces);
            return;
        }

        if (state == CompanionRoamState.Perch) FollowTarget(now, surfaces);
        else if ((state == CompanionRoamState.Walk || state == CompanionRoamState.Climb) && target != null)
            ValidateTarget(now, surfaces);
    }

    internal void Advance(double clipSeconds)
    {
        if (state is not (CompanionRoamState.Walk or CompanionRoamState.Climb or CompanionRoamState.Fall)) return;
        if (legDuration <= 0) return;
        double span = Math.Max(.01, legDuration - legLead - legTail);
        double progress = Math.Clamp((clipSeconds - legLead) / span, 0, 1);
        body = PlaceFeet(new(
            legFrom.X + (legTo.X - legFrom.X) * progress,
            legFrom.Y + (legTo.Y - legFrom.Y) * progress));
    }

    internal void ClipFinished(DateTime now, long token)
    {
        if (state == CompanionRoamState.Idle) return;
        if (token == 0 || token != activeToken) return;
        activeToken = 0;
        // A newer action was queued while this clip was still playing; that action
        // will start next, so this completion must not advance the new state.
        if (pending != null) return;
        switch (purpose)
        {
            case LegPurpose.Stroll:
            case LegPurpose.Approach:
            case LegPurpose.TopWalk:
                body = PlaceFeet(legTo);
                StartNextWalk(now);
                break;
            case LegPurpose.Climb:
                body = PlaceFeet(legTo);
                StartNextClimb(now);
                break;
            case LegPurpose.PerchIdle:
                if (now >= perchUntil)
                {
                    if (random.NextDouble() < options.PerchKnockChance) StartKnock(now);
                    else StartFall(now);
                }
                else QueueIdle(now);
                break;
            case LegPurpose.Knock:
                StartAngry(now);
                break;
            case LegPurpose.Angry:
                if (trip == CompanionRoamTrip.Perch) StartFall(now);
                else FinishTrip(now);
                break;
            case LegPurpose.Fall:
                body = PlaceFeet(legTo);
                if (settlePending) { settlePending = false; ResumeSettledTrip(now); }
                else FinishTrip(now);
                break;
        }
    }

    // ---- trip planning -------------------------------------------------

    // A trip always starts from the floor. If the pet was left in mid-air by a
    // drag or a display change she first floats down, then the next trip begins.
    private bool EnsureGround(DateTime now, CompanionRoamTrip kind, nint? handle)
    {
        if (lastWork.IsEmpty || Feet.Y >= lastWork.Bottom - 2) return true;
        if (state == CompanionRoamState.Fall && purpose == LegPurpose.Fall)
        {
            // Already floating down after an earlier request: remember the latest one.
            settleResume = kind;
            settleHandle = handle;
            return false;
        }
        settleResume = kind;
        settleHandle = handle;
        StartFall(now);
        if (state != CompanionRoamState.Fall)
        {
            settleResume = CompanionRoamTrip.None;
            settleHandle = null;
            return true;
        }
        settlePending = true;
        return false;
    }

    // After the settling fall lands, the trip that was asked for starts normally.
    private void ResumeSettledTrip(DateTime now)
    {
        var kind = settleResume;
        var handle = settleHandle;
        settleResume = CompanionRoamTrip.None;
        settleHandle = null;
        bool started = kind switch
        {
            CompanionRoamTrip.Stroll => TryStartStroll(now, lastWork, lastSurfaces),
            CompanionRoamTrip.Perch => TryStartPerch(now, lastWork, lastSurfaces, handle),
            CompanionRoamTrip.Push => TryStartPush(now, lastWork, lastSurfaces, handle),
            _ => false
        };
        if (!started) FinishTrip(now);
    }

    internal bool TryStartStroll(DateTime now, CompanionRect work, IReadOnlyList<CompanionSurface> surfaces)
    {
        if (body.IsEmpty) return false;
        Prepare(work, surfaces);
        if (!EnsureGround(now, CompanionRoamTrip.Stroll, null)) return true;
        trip = CompanionRoamTrip.Stroll;
        purpose = LegPurpose.Stroll;
        state = CompanionRoamState.Walk;
        target = null;
        path.Clear();
        var from = Feet;
        int stops = 1 + random.Next(3);
        for (int i = 0; i < stops; i++)
        {
            double x = work.Left + body.Width / 2 + random.NextDouble() * Math.Max(1, work.Width - body.Width);
            AppendPath(path, from, new(x, work.Bottom), MaxWalk);
            from = new(x, work.Bottom);
        }
        StartNextWalk(now);
        nextRoamAt = now + Span(options.DelayMin, options.DelayMax);
        return true;
    }

    internal bool TryStartPerch(DateTime now, CompanionRect work, IReadOnlyList<CompanionSurface> surfaces, nint? onlyHandle = null)
    {
        if (body.IsEmpty || !options.PerchEnabled) return false;
        Prepare(work, surfaces);
        if (!EnsureGround(now, CompanionRoamTrip.Perch, onlyHandle)) return true;
        var surface = onlyHandle is { } handle
            ? FindIn(surfaces, handle)
            : CompanionSurfacePolicy.ChoosePerch(surfaces, work, body.Width, body.Height, Feet.X, random);
        if (surface == null || !CompanionSurfacePolicy.CanPerch(surface, work, body.Width, body.Height)) return false;
        if (CompanionSurfacePolicy.ChooseSide(surface, work, body.Width, Feet.X) is not { } side) return false;

        target = surface;
        trip = CompanionRoamTrip.Perch;
        purpose = LegPurpose.Approach;
        state = CompanionRoamState.Walk;
        climbX = side.FeetX;
        path.Clear();
        AppendPath(path, Feet, new(climbX, work.Bottom), MaxWalk);
        StartNextWalk(now);
        nextRoamAt = now + Span(options.DelayMin, options.DelayMax);
        return true;
    }

    internal bool TryStartPush(DateTime now, CompanionRect work, IReadOnlyList<CompanionSurface> surfaces, nint? onlyHandle = null)
    {
        if (body.IsEmpty || !options.PushEnabled) return false;
        Prepare(work, surfaces);
        if (!EnsureGround(now, CompanionRoamTrip.Push, onlyHandle)) return true;
        var surface = onlyHandle is { } handle
            ? FindIn(surfaces, handle)
            : CompanionSurfacePolicy.ChoosePush(surfaces, work, body.Width, Feet.X, random);
        if (surface == null || !CompanionSurfacePolicy.CanPush(surface, work, body.Width)) return false;
        if (CompanionSurfacePolicy.ChooseSide(surface, work, body.Width, Feet.X) is not { } side) return false;

        target = surface;
        trip = CompanionRoamTrip.Push;
        purpose = LegPurpose.Approach;
        state = CompanionRoamState.Walk;
        climbX = side.FeetX;
        path.Clear();
        AppendPath(path, Feet, new(climbX, work.Bottom), MaxWalk);
        StartNextWalk(now);
        nextRoamAt = now + Span(options.DelayMin, options.DelayMax);
        return true;
    }

    private bool TryStartTrip(DateTime now, CompanionRect work, IReadOnlyList<CompanionSurface> surfaces)
    {
        if (options.PerchEnabled && random.NextDouble() < options.PerchChance
            && TryStartPerch(now, work, surfaces)) return true;
        if (options.PushEnabled && random.NextDouble() < options.PushChance
            && TryStartPush(now, work, surfaces)) return true;
        return false;
    }

    // ---- walking and climbing -----------------------------------------

    private void StartNextWalk(DateTime now)
    {
        while (path.Count > 0 && Feet.DistanceTo(path.Peek()) < 8) path.Dequeue();
        if (path.Count == 0) { OnWalkPathComplete(now); return; }
        legFrom = Feet;
        legTo = path.Dequeue();
        state = CompanionRoamState.Walk;
        legDuration = path.Count > 0 || purpose is LegPurpose.Approach or LegPurpose.TopWalk ? 7.2 : 10;
        legLead = 1.75;
        legTail = legDuration < 10 ? .4 : 4.8;
        legMirror = legTo.X > legFrom.X + .5;
        Request(CompanionRoamActions.Walk, legMirror, 1, legDuration < 10 ? legDuration : null);
    }

    private void OnWalkPathComplete(DateTime now)
    {
        switch (purpose)
        {
            case LegPurpose.Approach:
                if (trip == CompanionRoamTrip.Push) StartKnock(now);
                else StartClimb(now);
                break;
            case LegPurpose.TopWalk:
                BeginPerch(now);
                break;
            default:
                FinishTrip(now);
                break;
        }
    }

    private void StartClimb(DateTime now)
    {
        if (target == null) { FinishTrip(now); return; }
        var surface = Find(target.Handle);
        if (surface == null) { StartFall(now); return; }
        target = surface;
        double top = surface.Bounds.Top + options.PerchOverlap;
        if (Feet.Y - top < 24) { BeginTopWalk(now); return; }
        climbPath.Clear();
        AppendPath(climbPath, Feet, new(climbX, top), MaxClimb);
        StartNextClimb(now);
    }

    private void StartNextClimb(DateTime now)
    {
        while (climbPath.Count > 0 && Feet.DistanceTo(climbPath.Peek()) < 8) climbPath.Dequeue();
        if (climbPath.Count == 0) { BeginTopWalk(now); return; }
        legFrom = Feet;
        legTo = climbPath.Dequeue();
        double distance = legFrom.DistanceTo(legTo);
        legDuration = Math.Clamp(distance / Math.Max(1, options.ClimbUnitsPerSecond), 2.5, 9);
        legLead = Math.Min(1, legDuration * .2);
        legTail = legLead;
        legMirror = target != null && target.Bounds.CenterX > Feet.X;
        state = CompanionRoamState.Climb;
        Request(CompanionRoamActions.Float, legMirror, 1, legDuration < 9.95 ? legDuration : null);
    }

    private void BeginTopWalk(DateTime now)
    {
        if (target == null) { FinishTrip(now); return; }
        var surface = Find(target.Handle);
        if (surface == null) { StartFall(now); return; }
        target = surface;
        purpose = LegPurpose.TopWalk;
        state = CompanionRoamState.Walk;
        double x = CompanionSurfacePolicy.PerchX(surface, lastWork, body.Width);
        path.Clear();
        AppendPath(path, Feet, new(x, surface.Bounds.Top + options.PerchOverlap), MaxWalk);
        StartNextWalk(now);
    }

    private void BeginPerch(DateTime now)
    {
        var surface = FindTarget();
        if (surface == null) { StartFall(now); return; }
        target = surface;
        state = CompanionRoamState.Perch;
        purpose = LegPurpose.PerchIdle;
        body = PlaceOnPerch(surface);
        perchOffsetX = body.CenterX - surface.Bounds.Left;
        perchUntil = now + Span(options.PerchMin, options.PerchMax);
        QueueIdle(now);
    }

    private void QueueIdle(DateTime now)
    {
        string id = now.Hour is >= 23 or < 8
            ? CompanionRoamActions.Nap
            : random.NextDouble() < .5 ? CompanionRoamActions.Idle : CompanionRoamActions.Hum;
        Request(id);
    }

    private void StartKnock(DateTime now)
    {
        if (target == null) { FinishTrip(now); return; }
        state = CompanionRoamState.Push;
        purpose = LegPurpose.Knock;
        legMirror = target.Bounds.CenterX > Feet.X;
        Request(CompanionRoamActions.Push, legMirror, 1, 4.0);
    }

    private void StartAngry(DateTime now)
    {
        state = CompanionRoamState.Push;
        purpose = LegPurpose.Angry;
        Request(CompanionRoamActions.PushFail, false, 1, 5.0);
    }

    private void StartFall(DateTime now)
    {
        var from = Feet;
        double ground = lastWork.IsEmpty ? from.Y : lastWork.Bottom;
        double drop = ground - from.Y;
        target = null;
        if (drop <= 12) { FinishTrip(now); return; }
        state = CompanionRoamState.Fall;
        purpose = LegPurpose.Fall;
        legFrom = from;
        legTo = new(from.X, ground);
        legDuration = Math.Clamp(drop / 900, .6, 2.4);
        legLead = .08;
        legTail = .12;
        Request(CompanionRoamActions.Fall, false, 1, legDuration);
    }

    private void FinishTrip(DateTime now)
    {
        state = CompanionRoamState.Idle;
        trip = CompanionRoamTrip.None;
        purpose = LegPurpose.None;
        target = null;
        pending = null;
        path.Clear();
        climbPath.Clear();
        targetMissingSince = DateTime.MinValue;
        occludedSince = DateTime.MinValue;
        settlePending = false;
        settleResume = CompanionRoamTrip.None;
        settleHandle = null;
    }

    // ---- perching ------------------------------------------------------

    private void FollowTarget(DateTime now, IReadOnlyList<CompanionSurface> surfaces)
    {
        var surface = target == null ? null : FindIn(surfaces, target.Handle);
        if (surface == null)
        {
            if (targetMissingSince == DateTime.MinValue) targetMissingSince = now;
            if ((now - targetMissingSince).TotalSeconds >= 1.5) StartFall(now);
            return;
        }
        targetMissingSince = DateTime.MinValue;
        if (surface.Occluded)
        {
            if (occludedSince == DateTime.MinValue) occludedSince = now;
            if ((now - occludedSince).TotalSeconds >= 2.5) { StartFall(now); return; }
        }
        else occludedSince = DateTime.MinValue;
        target = surface;
        body = PlaceOnPerch(surface);
    }

    private void ValidateTarget(DateTime now, IReadOnlyList<CompanionSurface> surfaces)
    {
        if (target == null) return;
        var surface = FindIn(surfaces, target.Handle);
        if (surface == null)
        {
            if (targetMissingSince == DateTime.MinValue) targetMissingSince = now;
            if ((now - targetMissingSince).TotalSeconds >= 1.2) StartFall(now);
            return;
        }
        targetMissingSince = DateTime.MinValue;
        target = surface;
    }

    private CompanionSurface? FindTarget() => target == null ? null : Find(target.Handle);

    private CompanionSurface? Find(nint handle) => FindIn(lastSurfaces, handle);

    private static CompanionSurface? FindIn(IReadOnlyList<CompanionSurface> surfaces, nint handle)
    {
        for (int i = 0; i < surfaces.Count; i++)
            if (surfaces[i].Handle == handle) return surfaces[i];
        return null;
    }

    private CompanionRect PlaceOnPerch(CompanionSurface surface)
    {
        double x = surface.Bounds.Left + perchOffsetX;
        double minimum = surface.Bounds.Left + body.Width * .2;
        double maximum = surface.Bounds.Right - body.Width * .2;
        if (maximum < minimum) x = surface.Bounds.CenterX;
        else x = Math.Clamp(x, minimum, maximum);
        return PlaceFeet(new(x, surface.Bounds.Top + options.PerchOverlap));
    }

    // ---- geometry ------------------------------------------------------

    private void Prepare(CompanionRect work, IReadOnlyList<CompanionSurface> surfaces)
    {
        lastWork = work;
        lastSurfaces = surfaces;
        targetMissingSince = DateTime.MinValue;
        occludedSince = DateTime.MinValue;
        climbPath.Clear();
    }

    private CompanionRect PlaceFeet(CompanionPoint feet)
    {
        if (body.IsEmpty) return body;
        if (lastWork.IsEmpty)
            return new(feet.X - body.Width / 2, feet.Y - body.Height, body.Width, body.Height);
        double x = Math.Clamp(feet.X - body.Width / 2, lastWork.Left, Math.Max(lastWork.Left, lastWork.Right - body.Width));
        double y = Math.Clamp(feet.Y - body.Height, lastWork.Top, Math.Max(lastWork.Top, lastWork.Bottom - body.Height));
        return new(x, y, body.Width, body.Height);
    }

    private double MaxWalk => Math.Max(40, body.Width * options.MaxWalkFactor);
    private double MaxClimb => Math.Max(60, body.Height * options.MaxClimbFactor);

    private static void AppendPath(Queue<CompanionPoint> queue, CompanionPoint from, CompanionPoint to, double maxStep)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(1, maxStep)));
        for (int i = 1; i <= steps; i++)
            queue.Enqueue(new(from.X + dx * i / steps, from.Y + dy * i / steps));
    }

    private TimeSpan Span(TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum <= minimum) return minimum;
        return minimum + TimeSpan.FromMilliseconds(random.NextDouble() * (maximum - minimum).TotalMilliseconds);
    }
}
