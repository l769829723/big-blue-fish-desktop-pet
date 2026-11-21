using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace BigBlueFish.Windows;

// Hosts the shared roaming engine inside the WPF companion window. The engine owns
// the visible-body rectangle; this file converts it to/from window coordinates and
// plays the reused clips it asks for. No window belonging to another application is
// ever moved: pushing a window is a purely cosmetic animation.
public partial class MainWindow
{
    private CompanionRoaming? roaming;
    private CompanionRoamingAction? roamAction;
    private IReadOnlyList<CompanionSurface> roamSurfaces = Array.Empty<CompanionSurface>();
    private DateTime nextRoamScan = DateTime.MinValue;

    private void StartRoaming()
    {
        if (roaming != null || videoPlayer == null) return;
        roaming = new CompanionRoaming(companionRandom);
        SyncRoamBody();
        roaming.ResetSchedule(DateTime.UtcNow);
        videoPlayer.FramePresented += AdvanceRoamingFrame;
    }

    private void SyncRoamBody()
    {
        if (roaming == null) return;
        roaming.Sync(CompanionWindowsSurfaces.VisibleBody(this));
    }

    private bool RoamingAllowed()
    {
        if (!preferences.RoamingEnabled || !companionReady || companionClosed || exitAnimation) return false;
        if (hiddenByUser || fullscreenHidden || dragging || PuppetActive) return false;
        if (companionSettings != null || modelInstaller != null || updateWindow != null) return false;
        if (!IsVisible) return false;
        // Only start a trip from a quiet pose; a trip already under way may span
        // several clips, so the roam phase must count as quiet too.
        bool roamingNow = roaming?.IsRoaming == true;
        // A cancelled trip leaves its clip playing until it ends; that parked clip
        // must not block the next trip.
        bool parkedClip = roamAction == null && animationPhase == "Roam";
        return roamingNow || parkedClip || animationPhase is "Idle" or "BetweenMoves";
    }

    private void TickRoaming(DateTime now)
    {
        if (roaming == null) return;
        if (!RoamingAllowed())
        {
            CancelRoaming();
            return;
        }

        // Window enumeration costs a DWM call per window, so idle time only pays
        // for it when the next trip is actually due.
        if (now >= nextRoamScan && (roaming.State != CompanionRoamState.Idle || now >= roaming.NextRoamAt))
        {
            nextRoamScan = now.AddMilliseconds(200);
            roamSurfaces = CompanionWindowsSurfaces.Enumerate(this, new WindowInteropHelper(this).Handle);
        }

        // The bubble can resize the window between frames, which shifts the visible
        // body inside it; resynchronise before planning so the engine and window agree.
        SyncRoamBody();
        roaming.Tick(now, CompanionWindowsSurfaces.WorkArea(this), roamSurfaces, true, CompanionNative.IdleSeconds);

        if (roamAction != null) return;
        if (roaming.TakeAction() is { } action) PlayRoamAction(action);
        else if (roaming.IsRoaming) ApplyRoamBody();
    }

    private void CancelRoaming()
    {
        if (roamAction == null && roaming is not { IsRoaming: true }) return;
        roamAction = null;
        roaming?.Cancel();
        CompanionEdgeLayout.SuspendLayout = false;
    }

    // Switched-off roaming must not strand the pet on a window she can no longer
    // leave, so a perch is turned into a plain landing on the floor.
    private void CancelRoamingToGround()
    {
        CancelRoaming();
        if (roaming == null || !roaming.MoveToGround()) return;
        ApplyRoamBody();
        ClampCompanion();
        CompanionEdgeLayout.Refresh(this);
        anchor = new Point(Left, Top);
        SaveCompanion();
        SyncRoamBody();
    }

    private void PlayRoamAction(CompanionRoamingAction action)
    {
        if (videoPlayer == null || animationPhase == "Exit") return;
        var rule = animationPolicy.Find(action.Id);
        roamAction = action;
        EndGreetingNotice();
        DiagnosticLog.Write(DiagnosticEvent.AnimationStarted, kind: "Roam", action: rule.Id);
        animationPhase = "Roam";
        currentAction = rule.Id;
        // Every frame repositions the layered window. The bubble is anchored inside
        // the window, so only its own size changes need a layout pass.
        CompanionEdgeLayout.SuspendLayout = true;
        videoPlayer.Play(Path.Combine(AnimationDirectory, rule.File), action.Mirror, AnimationFinished,
            startSeconds: 0, endSeconds: action.EndSeconds, playbackRate: action.PlaybackRate);
    }

    private void AdvanceRoamingFrame(double seconds)
    {
        if (roamAction == null || roaming == null) return;
        roaming.Advance(seconds);
        ApplyRoamBody();
    }

    private void ApplyRoamBody()
    {
        if (roaming == null) return;
        CompanionWindowsSurfaces.PlaceBody(this, roaming.Body);
    }

    private void FinishRoamClip()
    {
        var action = roamAction;
        roamAction = null;
        if (roaming == null) { AnimationBoundary(); return; }
        roaming.ClipFinished(DateTime.UtcNow, action?.Token ?? 0);
        // Hand over to the next clip before snapping the window back: an action
        // queued mid-clip (for example a settling fall) must keep its planned start.
        if (roaming.TakeAction() is { } next) { PlayRoamAction(next); return; }
        CompanionEdgeLayout.SuspendLayout = false;
        // The engine always lands on a legal spot; Clamp only absorbs edge snapping
        // and any height change caused by a bubble that appeared mid-trip.
        ClampCompanion();
        CompanionEdgeLayout.Refresh(this);
        anchor = new Point(Left, Top);
        SaveCompanion();
        SyncRoamBody();
        if (roaming.IsRoaming) { ApplyRoamBody(); return; }
        AnimationBoundary();
    }
}
