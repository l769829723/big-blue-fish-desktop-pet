using System;
using System.Collections.Generic;
using System.Linq;

namespace BigBlueFish.Windows;

// Screen-space geometry shared by Windows (DIPs) and Linux (pixels). The platform
// converts native window rectangles into these units before handing them to the
// roaming engine, so the planner never has to know about DPI or display servers.
internal readonly record struct CompanionPoint(double X, double Y)
{
    internal double DistanceTo(CompanionPoint other)
    {
        double dx = X - other.X, dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

internal readonly record struct CompanionRect(double X, double Y, double Width, double Height)
{
    internal double Left => X;
    internal double Top => Y;
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal double CenterX => X + Width / 2;
    internal double CenterY => Y + Height / 2;
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    internal static CompanionRect FromLTRB(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    internal CompanionRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    internal CompanionRect WithX(double x) => new(x, Y, Width, Height);

    internal CompanionRect WithY(double y) => new(X, y, Width, Height);

    internal bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    internal bool IntersectsWith(CompanionRect other) =>
        other.Left < Right && other.Right > X && other.Top < Bottom && other.Bottom > Y;
}

// A real, visible top-level window as seen by the platform provider. Order 0 is the
// topmost window; Occluded means another window covers the point where the pet would
// sit, so the surface is unsuitable as a perch.
internal sealed record CompanionSurface(
    nint Handle,
    CompanionRect Bounds,
    string Title,
    int ProcessId,
    int Order,
    bool IsForeground = false,
    bool Occluded = false)
{
    internal bool SameAs(CompanionSurface? other) => other != null && other.Handle == Handle;
}

internal readonly record struct CompanionSurfaceSide(double FeetX, bool WindowToRight);

internal static class CompanionSurfacePolicy
{
    // The pet needs its whole body to fit above the window's top edge. Windows near
    // the top of the work area are skipped rather than drawn half off-screen.
    internal static bool CanPerch(CompanionSurface surface, CompanionRect work, double bodyWidth, double bodyHeight)
    {
        var bounds = surface.Bounds;
        if (surface.Occluded || bounds.IsEmpty) return false;
        if (bounds.Width < bodyWidth * 1.1) return false;
        if (bounds.Top - work.Top < bodyHeight + 2) return false;
        if (bounds.Top >= work.Bottom - bodyHeight * .25) return false;
        return true;
    }

    // Only windows that reach close to the floor can be pushed from the ground.
    internal static bool CanPush(CompanionSurface surface, CompanionRect work, double bodyWidth)
    {
        if (surface.Occluded || surface.IsForeground || surface.Bounds.IsEmpty) return false;
        if (surface.Bounds.Width < bodyWidth) return false;
        return surface.Bounds.Bottom >= work.Bottom - bodyWidth * .5;
    }

    // The pet climbs the left or right side of a window, overlapping the edge a
    // little so her feet read as resting on it. Picks the side closer to her.
    internal static CompanionSurfaceSide? ChooseSide(CompanionSurface surface, CompanionRect work, double bodyWidth, double fromX)
    {
        double left = surface.Bounds.Left - bodyWidth * .25;
        double right = surface.Bounds.Right + bodyWidth * .25;
        bool leftFits = left - bodyWidth / 2 >= work.Left - .5;
        bool rightFits = right + bodyWidth / 2 <= work.Right + .5;
        if (!leftFits && !rightFits) return null;
        if (leftFits && rightFits)
        {
            bool leftCloser = Math.Abs(fromX - left) <= Math.Abs(fromX - right);
            return leftCloser ? new(left, true) : new(right, false);
        }
        return leftFits ? new(left, true) : new(right, false);
    }

    internal static CompanionSurface? ChoosePerch(
        IReadOnlyList<CompanionSurface> surfaces,
        CompanionRect work,
        double bodyWidth,
        double bodyHeight,
        double fromX,
        Random random)
    {
        var candidates = surfaces
            .Where(surface => CanPerch(surface, work, bodyWidth, bodyHeight)
                && ChooseSide(surface, work, bodyWidth, fromX) != null)
            .ToArray();
        return Weighted(candidates, work, fromX, random);
    }

    internal static CompanionSurface? ChoosePush(
        IReadOnlyList<CompanionSurface> surfaces,
        CompanionRect work,
        double bodyWidth,
        double fromX,
        Random random)
    {
        var candidates = surfaces
            .Where(surface => CanPush(surface, work, bodyWidth)
                && ChooseSide(surface, work, bodyWidth, fromX) != null)
            .ToArray();
        return Weighted(candidates, work, fromX, random);
    }

    private static CompanionSurface? Weighted(CompanionSurface[] candidates, CompanionRect work, double fromX, Random random)
    {
        if (candidates.Length == 0) return null;
        var weights = candidates.Select(surface =>
        {
            double stack = 1.0 / (1 + Math.Max(0, surface.Order));
            double foreground = surface.IsForeground ? .35 : 1.0;
            double distance = 1.0 / (1 + Math.Abs(surface.Bounds.CenterX - fromX) / Math.Max(1, work.Width));
            return stack * foreground * distance;
        }).ToArray();
        double total = weights.Sum();
        if (total <= 0) return candidates[random.Next(candidates.Length)];
        double point = random.NextDouble() * total;
        for (int i = 0; i < candidates.Length; i++)
        {
            point -= weights[i];
            if (point < 0) return candidates[i];
        }
        return candidates[^1];
    }

    internal static double PerchX(CompanionSurface surface, CompanionRect work, double bodyWidth)
    {
        double minimum = surface.Bounds.Left + bodyWidth * .25;
        double maximum = surface.Bounds.Right - bodyWidth * .25;
        double x = maximum < minimum ? surface.Bounds.CenterX : Math.Clamp(surface.Bounds.CenterX, minimum, maximum);
        return Math.Clamp(x, work.Left + bodyWidth / 2, Math.Max(work.Left + bodyWidth / 2, work.Right - bodyWidth / 2));
    }
}
