using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Windows;

// Positions the visible character at the monitor edge and keeps its bubble in the capturable owner window.
public static class CompanionEdgeLayout
{
    private static int bubbleVisible;
    private static long bubbleHandle;
    internal static bool HasVisibleBubble => System.Threading.Volatile.Read(ref bubbleVisible) != 0;
    internal static IntPtr BubbleWindowHandle => new(System.Threading.Interlocked.Read(ref bubbleHandle));
    private sealed class Follow
    {
        private readonly Window pet;
        private readonly Border bubble;
        private readonly System.Windows.Controls.TextBlock text;
        private readonly System.Windows.Controls.TextBlock measuringText = new();
        private readonly DependencyPropertyDescriptor textChange;
        private bool updating;
        private bool closed;
        private bool queued;
        private bool petPinned;
        private DateTime nextDesktopCheck;
        private DateTime nextPinAttempt;
        private readonly DependencyPropertyDescriptor visibility;
        public Follow(Window w)
        {
            pet = w;
            bubble = ((BigBlueFish.Windows.MainWindow)w).CompanionBubble;
            text = ((BigBlueFish.Windows.MainWindow)w).CompanionBubbleText;
            textChange = DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.TextBlock.TextProperty, typeof(System.Windows.Controls.TextBlock));
            textChange.AddValueChanged(text, Changed);
            visibility = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
            visibility.AddValueChanged(bubble, Changed);
            pet.Loaded += Loaded;
            pet.LocationChanged += Changed;
            pet.SizeChanged += Changed;
            pet.IsVisibleChanged += Visible;
            pet.DpiChanged += DpiChanged;
            pet.Closed += Closed;
            bubble.SizeChanged += Changed;
            if (pet.IsLoaded)
            {
                Create();
                TryPinWindows();
            }
        }

        private void Loaded(object s, RoutedEventArgs e)
        {
            Create();
            TryPinWindows();
            Update();
        }

        private void Changed(object? s, EventArgs e)
        {
            System.Threading.Volatile.Write(ref bubbleVisible, bubble.Visibility == Visibility.Visible ? 1 : 0);
            Update();
            QueueUpdate();
        }

        private void QueueUpdate()
        {
            if (!queued && !closed)
            {
                queued = true;
                pet.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    queued = false;
                    Update();
                }));
            }
        }

        private void Visible(object s, DependencyPropertyChangedEventArgs e)
        {
            System.Threading.Volatile.Write(ref bubbleVisible, bubble.Visibility == Visibility.Visible ? 1 : 0);
            Update();
        }

        private void DpiChanged(object s, System.Windows.DpiChangedEventArgs e)
        {
            Clamp(pet);
        }

        private void Closed(object? s, EventArgs e)
        {
            closed = true;
            visibility.RemoveValueChanged(bubble, Changed);
            textChange.RemoveValueChanged(text, Changed);
            System.Threading.Interlocked.CompareExchange(ref bubbleHandle, 0, new WindowInteropHelper(pet).Handle.ToInt64());
        }

        private void Create()
        {
            if (!closed)
            {
                bubble.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                bubble.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                System.Threading.Interlocked.Exchange(ref bubbleHandle, new WindowInteropHelper(pet).EnsureHandle().ToInt64());
            }
        }

        public void Update()
        {
            if (updating || closed)
            {
                return;
            }

            updating = true;
            try
            {
                nint handle = new WindowInteropHelper(pet).EnsureHandle();
                System.Threading.Interlocked.Exchange(ref bubbleHandle, handle.ToInt64());
                if (!pet.IsVisible || !CompanionNative.IsOnCurrentVirtualDesktop(handle) || bubble.Visibility != Visibility.Visible)
                {
                    System.Threading.Volatile.Write(ref bubbleVisible, 0);
                    ResizeWindowForBubble(0);
                    return;
                }

                Rect area = WorkArea(pet);
                var styledBubble = bubble as BigBlueFish.Windows.CompanionBubble;
                double scale = ((BigBlueFish.Windows.MainWindow)pet).CompanionBubbleScale;
                Rect petBounds = PetBounds(pet);
                double maximumWidth = Math.Max(1.0, Math.Min(300.0, (area.Width - 16.0) / scale));
                bubble.MaxWidth = maximumWidth;
                measuringText.Text = ((BigBlueFish.Windows.MainWindow)pet).CompanionBubbleLayoutText;
                measuringText.FontFamily = text.FontFamily;
                measuringText.FontSize = text.FontSize;
                measuringText.FontWeight = text.FontWeight;
                measuringText.TextWrapping = text.TextWrapping;
                measuringText.Margin = text.Margin;
                double horizontal = bubble.Padding.Left + bubble.Padding.Right + bubble.BorderThickness.Left + bubble.BorderThickness.Right;
                double vertical = bubble.Padding.Top + bubble.Padding.Bottom + bubble.BorderThickness.Top + bubble.BorderThickness.Bottom;
                measuringText.Measure(new System.Windows.Size(Math.Max(1, maximumWidth - horizontal), double.PositiveInfinity));
                double naturalWidth = Math.Min(maximumWidth, Math.Max(26, measuringText.DesiredSize.Width) + horizontal);
                measuringText.Measure(new System.Windows.Size(Math.Max(1, naturalWidth - horizontal - 2), double.PositiveInfinity));
                double naturalHeight = Math.Min(Math.Max(1.0, (area.Height - 16.0) / scale),
                    Math.Max(22, measuringText.DesiredSize.Height) + vertical);
                double bubbleWidth = naturalWidth * scale;
                double bubbleHeight = naturalHeight * scale;
                ResizeWindowForBubble(bubbleHeight + 10.0);
                petBounds = PetBounds(pet);
                area = WorkArea(pet);
                double petCenterX = petBounds.Left + petBounds.Width / 2.0;
                double left = Limit(petCenterX - bubbleWidth / 2.0, 8.0, pet.Width - bubbleWidth - 8.0);
                double top = petBounds.Top - bubbleHeight - 10.0;
                double maximumTop = Math.Max(0, pet.Height - bubbleHeight - 8.0);
                double minimumVisibleTop = Math.Max(0, area.Top + 8.0 - pet.Top);
                top = Math.Min(maximumTop, Math.Max(minimumVisibleTop, Math.Max(0, top)));
                bubble.MaxHeight = Math.Max(1.0, Math.Min(naturalHeight, (pet.Height - top - 8.0) / scale));
                bubble.Width = naturalWidth;
                bubble.Margin = new Thickness(left, top, 0, 0);
                bool belowPet = top > petBounds.Top - bubbleHeight - 10.0;
                styledBubble?.PointAt((petCenterX - left) / scale, belowPet);
                System.Threading.Volatile.Write(ref bubbleVisible, 1);
            }
            finally
            {
                updating = false;
            }
        }

        private void ResizeWindowForBubble(double bubbleHeight)
        {
            double baseHeight = SpriteSize(pet) + 35.0;
            Rect oldBounds = PetBounds(pet);
            double characterTop = pet.Top + oldBounds.Top;
            double baseTop = baseHeight - SpriteSize(pet) + 58.0;
            double extraHeight = Math.Max(0, bubbleHeight - baseTop);
            double targetHeight = baseHeight + extraHeight;
            if (Math.Abs(pet.Height - targetHeight) < 0.5) return;
            pet.Height = targetHeight;
            pet.Top = characterTop - PetBounds(pet).Top;
        }

        public void Hide()
        {
            System.Threading.Volatile.Write(ref bubbleVisible, 0);
        }

        public void CheckDesktop()
        {
            if (closed || DateTime.UtcNow < nextDesktopCheck) return;
            nextDesktopCheck = DateTime.UtcNow.AddMilliseconds(300);
            IntPtr petHandle = new WindowInteropHelper(pet).Handle;
            if (!CompanionNative.IsOnCurrentVirtualDesktop(petHandle))
            {
                Hide();
                CompanionNative.MoveToCurrentVirtualDesktop(petHandle);
                return;
            }
            if (pet.IsVisible && bubble.Visibility == Visibility.Visible) Update();
        }

        private void TryPinWindows()
        {
            if (petPinned || DateTime.UtcNow < nextPinAttempt) return;
            nextPinAttempt = DateTime.UtcNow.AddSeconds(3);
            petPinned = CompanionNative.PinWindowToAllVirtualDesktops(new WindowInteropHelper(pet).Handle);
        }
    }

    private static readonly ConditionalWeakTable<Window, Follow> followers = new ConditionalWeakTable<Window, Follow>();
    public static double SpriteSize(Window w)
    {
        return ((BigBlueFish.Windows.MainWindow)w).CompanionSpriteSize;
    }

    public static Rect PetBounds(Window w)
    {
        return ((BigBlueFish.Windows.MainWindow)w).CompanionVisibleBounds;
    }

    public static Rect WorkArea(Window w)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(w);
        Rect petBounds = PetBounds(w);
        if (PresentationSource.FromVisual(w) != null)
        {
            System.Windows.Point screenCenter = w.PointToScreen(new System.Windows.Point(petBounds.Left + petBounds.Width / 2.0, petBounds.Top + petBounds.Height / 2.0));
            Rectangle workingArea = Screen.FromPoint(new System.Drawing.Point((int)screenCenter.X, (int)screenCenter.Y)).WorkingArea;
            System.Windows.Point localTopLeft = w.PointFromScreen(new System.Windows.Point(workingArea.Left, workingArea.Top));
            System.Windows.Point localBottomRight = w.PointFromScreen(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
            return new Rect(w.Left + localTopLeft.X, w.Top + localTopLeft.Y, localBottomRight.X - localTopLeft.X, localBottomRight.Y - localTopLeft.Y);
        }

        Rectangle primaryArea = (Screen.PrimaryScreen ?? throw new InvalidOperationException("无法找到主显示器")).WorkingArea;
        return new Rect((double)primaryArea.Left / dpi.DpiScaleX, (double)primaryArea.Top / dpi.DpiScaleY, (double)primaryArea.Width / dpi.DpiScaleX, (double)primaryArea.Height / dpi.DpiScaleY);
    }

    private static double Limit(double v, double min, double max)
    {
        return Math.Clamp(double.IsFinite(v) ? v : min, min, Math.Max(min, max));
    }

    public static void Clamp(Window w)
    {
        Follow follower = followers.GetValue(w, (Window x) => new Follow(x));
        Rect area = WorkArea(w);
        Rect petBounds = PetBounds(w);
        double left = Limit(w.Left, area.Left - petBounds.Left, area.Right - petBounds.Right);
        double top = Limit(w.Top, area.Top - petBounds.Top, area.Bottom - petBounds.Bottom);
        if (Math.Abs(left + petBounds.Left - area.Left) < 14.0)
        {
            left = area.Left - petBounds.Left;
        }

        if (Math.Abs(left + petBounds.Right - area.Right) < 14.0)
        {
            left = area.Right - petBounds.Right;
        }

        if (Math.Abs(top + petBounds.Top - area.Top) < 14.0)
        {
            top = area.Top - petBounds.Top;
        }

        if (Math.Abs(top + petBounds.Bottom - area.Bottom) < 14.0)
        {
            top = area.Bottom - petBounds.Bottom;
        }

        w.Left = left;
        w.Top = top;
        follower.Update();
    }

    public static void Refresh(Window w) => followers.GetValue(w, x => new Follow(x)).Update();
    public static void Hide(Window w)
    {
        if (followers.TryGetValue(w, out var follower)) follower.Hide();
    }
    public static void CheckVirtualDesktop(Window w)
    {
        if (followers.TryGetValue(w, out var follower)) follower.CheckDesktop();
    }
}
