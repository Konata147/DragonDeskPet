using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragonDeskPet;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using Color = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;

internal static class PetInteractionTests
{
    public static void CheckBehavior()
    {
        CheckFeedbackOwnership();
        CheckSuppression();
        CheckStrokeRecognition();
        CheckTreatDrop();
        CheckClipTiming();
        CheckPlayerLifecycle();
        CheckDirectPressGestures();
    }

    public static int Run(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                CheckBehavior();
                CheckSettings(directory);
                CheckAnimationClocks();
                CheckArtwork();
                CheckCompanionArtwork(directory);
                RenderUi(directory);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) { Console.WriteLine(failure); return 1; }
        Console.WriteLine("PASS: distinct artwork, stroke recognition, finite animation timing, motion limits, settings and scaled WPF layout.");
        return 0;
    }

    private static void CheckFeedbackOwnership()
    {
        var machine = new PetStateMachine();
        machine.TransitionTo(PetState.Happy);
        var first = machine.Revision;
        machine.TransitionTo(PetState.Happy, restart: true);
        var second = machine.Revision;
        Require(!machine.TryFinishFeedback(first), "Old action ended a newer action of the same state.");
        Require(machine.TryFinishFeedback(second, true) && machine.Current == PetState.Hover,
            "Current action should restore hover.");
        foreach (var state in new[] { PetState.Thinking, PetState.Dragged, PetState.Sleeping })
        {
            machine.TransitionTo(PetState.Happy, restart: true);
            var revision = machine.Revision;
            machine.TransitionTo(state);
            Require(!machine.TryFinishFeedback(revision) && machine.Current == state,
                "Feedback interrupted a higher priority state.");
        }
        Console.WriteLine("PASS: repeated actions and stale feedback cannot interrupt thinking, dragging or sleep.");
    }

    private static void CheckSuppression()
    {
        Require(PetActivities.CanPlayAmbient(PetState.Idle, false, false, true, false, false, false, true),
            "Idle visible pet should allow ambient actions.");
        for (var block = 0; block < 7; block++)
            Require(!PetActivities.CanPlayAmbient(PetState.Idle, block == 0, block == 1, block != 2,
                block == 3, block == 4, block == 5, block != 6), "An ambient suppression condition was ignored.");
        foreach (var state in Enum.GetValues<PetState>().Where(s => s != PetState.Idle))
            Require(!PetActivities.CanPlayAmbient(state, false, false, true, false, false, false, true),
                "Ambient action should only start from idle.");
        Require(!PetActivities.CanStart(PetState.Thinking, false, false, true), "Thinking was interrupted.");
        Require(!PetActivities.CanStart(PetState.Dragged, false, false, true), "Drag was interrupted.");
        Require(PetActivities.CanStart(PetState.Sleeping, false, false, true), "Manual wake should be possible.");
        Console.WriteLine("PASS: hidden/fullscreen, focus, busy, drag, open panel and reduced-motion suppression.");
    }

    private static void CheckSettings(string directory)
    {
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{\"Scale\":1.3,\"Provider\":\"Offline\"}")!;
        Require(oldSettings.AmbientPetActionsEnabled && !oldSettings.ReducePetMotion, "Legacy settings defaults lost.");
        var service = new SettingsService(Path.Combine(directory, "isolated-settings"));
        oldSettings.AmbientPetActionsEnabled = false;
        oldSettings.ReducePetMotion = true;
        service.Save(oldSettings);
        var loaded = service.Load();
        Require(!loaded.AmbientPetActionsEnabled && loaded.ReducePetMotion && loaded.Scale == 1.3,
            "Interaction preferences did not persist.");
        Console.WriteLine("PASS: existing settings remain compatible and both preferences persist locally.");
    }

    private static void CheckAnimationClocks()
    {
        var scale = new ScaleTransform(); var rotation = new RotateTransform(); var translation = new TranslateTransform();
        var accent = new TextBlock();
        var animator = new PetAnimator(scale, rotation, translation, accent);
        foreach (var activity in Enum.GetValues<PetActivity>())
        {
            animator.Play(activity, false);
            Require(!rotation.HasAnimatedProperties && !translation.HasAnimatedProperties && !scale.HasAnimatedProperties
                || activity is PetActivity.Hop or PetActivity.Land, $"Whole body sway returned for {activity}.");
            if (activity == PetActivity.Hop)
                Require(translation.HasAnimatedProperties && !rotation.HasAnimatedProperties && !scale.HasAnimatedProperties,
                    "Jump must only move vertically once.");
            if (activity == PetActivity.Land)
                Require(scale.HasAnimatedProperties && !rotation.HasAnimatedProperties && !translation.HasAnimatedProperties,
                    "Landing must only compress vertically once.");
            animator.Stop();
            Require(!scale.HasAnimatedProperties && !rotation.HasAnimatedProperties && !translation.HasAnimatedProperties
                && !accent.HasAnimatedProperties && scale.ScaleX == 1 && scale.ScaleY == 1 && rotation.Angle == 0
                && translation.X == 0 && translation.Y == 0, "Stopping left a visual clock or transform behind.");
            animator.Play(activity, true);
            Require(!scale.HasAnimatedProperties && !rotation.HasAnimatedProperties && !translation.HasAnimatedProperties
                && !accent.HasAnimatedProperties && accent.Text.Length > 0, "Reduced motion should keep static feedback only.");
        }
        foreach (var state in Enum.GetValues<PetState>())
        {
            animator.ShowState(state, false, false);
            Require(!rotation.HasAnimatedProperties && !translation.HasAnimatedProperties && !scale.HasAnimatedProperties,
                $"Idle, hover, thinking or dragging still moves the whole image in state {state}.");
            animator.ShowState(state, true, false);
            Require(!scale.HasAnimatedProperties && !rotation.HasAnimatedProperties && !translation.HasAnimatedProperties
                && !accent.HasAnimatedProperties, "Reduced motion left a state animation running.");
        }
        animator.ShowState(PetState.Idle, false, true);
        Require(!translation.HasAnimatedProperties && !rotation.HasAnimatedProperties, "Focus should not bounce or sway.");
        animator.PlayHopMotion(1);
        Require(translation.HasAnimatedProperties && !rotation.HasAnimatedProperties
            && !scale.HasAnimatedProperties && translation.X == 0,
            "The full-frame jump must rise vertically without a sideways clock.");
        animator.Stop();
        Console.WriteLine("PASS: ordinary actions stay planted, jump/landing are vertical and finite, reduced motion is static.");
    }

    private static void CheckStrokeRecognition()
    {
        var stroke = new PetStrokeRecognizer();
        Require(!stroke.Observe(50, true, true, 100), "Single sample triggered a pat.");
        Require(!stroke.Observe(62, true, true, 300), "Straight motion triggered a pat.");
        Require(stroke.Observe(38, true, true, 500), "A reversed 36-unit head stroke should trigger.");
        Require(!stroke.Observe(50, true, true, 700) && !stroke.Observe(20, true, true, 900),
            "Three-second cooldown failed.");
        stroke.Reset();
        Require(!stroke.Observe(50, false, true, 4000), "Outside-head movement triggered.");
        Require(!stroke.Observe(50, true, false, 4100), "Ineligible state triggered.");
        Require(!stroke.Observe(50, true, true, 5000) && !stroke.Observe(73, true, true, 5900)
            && !stroke.Observe(42, true, true, 6100), "Stroke should expire after one second.");
        Console.WriteLine("PASS: head-stroke reversal, distance, time window, eligibility and cooldown.");
    }

    private static void CheckTreatDrop()
    {
        foreach (var scale in new[] { .6, 1.0, 2.0 })
        {
            Require(PetTreatDrop.IsAccepted(true, true, true, 23.9 * scale, scale),
                "Mouth target should accept a treat at the supported scale.");
            Require(!PetTreatDrop.IsAccepted(true, true, true, 24.1 * scale, scale),
                "Release outside mouth target should cancel feeding.");
            Require(!PetTreatDrop.IsAccepted(false, true, true, 0, scale)
                && !PetTreatDrop.IsAccepted(true, false, true, 0, scale)
                && !PetTreatDrop.IsAccepted(true, true, false, 0, scale),
                "Cancelled, unheld or ineligible treat must not feed.");
        }
        Console.WriteLine("PASS: treat drop accepts only held releases at the scaled mouth target.");
    }

    private static void CheckClipTiming()
    {
        var clip = new PetAnimationClip
        { Id = "Sample", Frames = ["00.png", "01.png", "02.png"], DurationsMs = [100, 120, 90], PosterFrame = 1 };
        Require(clip.IsValid && clip.FrameAt(0) == 0 && clip.FrameAt(99) == 0 && clip.FrameAt(100) == 1
            && clip.FrameAt(219) == 1 && clip.FrameAt(220) == 2, "Frame timing boundaries failed.");
        clip.Frames = ["../private.png"];
        Require(!clip.IsValid, "Frame path traversal should be rejected.");
        Console.WriteLine("PASS: finite frame durations and local asset path validation.");
    }

    private static void CheckPlayerLifecycle()
    {
        var clip = new PetAnimationClip
        { Id = "Sample", Frames = ["0.png", "1.png"], DurationsMs = [100, 100], PosterFrame = 1 };
        var frame = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 255, 255, 255, 255 }, 4);
        frame.Freeze();
        var animation = new LoadedPetAnimation(clip, [frame, frame]);
        using var player = new PetAnimationPlayer();
        var shown = 0; var completed = 0;
        player.FrameChanged += _ => shown++;
        var advance = typeof(PetAnimationPlayer).GetMethod("Advance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        player.Play(animation, false, () => completed++);
        advance.Invoke(player, [100L]);
        advance.Invoke(player, [200L]);
        Require(shown == 2 && completed == 1 && !player.IsPlaying,
            "Finite clip did not stop and complete exactly once.");
        player.Play(animation, false, () => completed++);
        player.Stop(); advance.Invoke(player, [200L]);
        Require(completed == 1 && !player.IsPlaying, "Stopped clip completed after interruption.");
        player.Play(animation, true, () => completed++);
        Require(shown == 4 && completed == 1 && !player.IsPlaying,
            "Reduced-motion clip must show only its poster frame.");
        Console.WriteLine("PASS: player stops at the final frame, ignores interrupted callbacks and shows one static poster.");
    }

    private static void CheckDirectPressGestures()
    {
        var hold = new PetHoldRecognizer();
        hold.Begin(1000, true);
        Require(!hold.TryTrigger(1549, true, true, true) && hold.IsArmed,
            "A short click was mistaken for a cuddle.");
        Require(hold.TryTrigger(1550, true, true, true)
            && !hold.TryTrigger(2000, true, true, true),
            "Long press did not trigger once at 550 ms.");
        hold.Begin(3000, true);
        Require(!hold.TryTrigger(3550, true, false, true) && !hold.IsArmed,
            "A drag kept the long-press timer armed.");
        hold.Begin(4000, false);
        Require(!hold.TryTrigger(4600, true, true, true),
            "Double-click or ineligible state armed a cuddle.");
        foreach (var cancelled in new[] { (false, true), (true, false) })
        {
            hold.Begin(5000, true);
            Require(!hold.TryTrigger(5600, cancelled.Item1, true, cancelled.Item2),
                "Release or interrupted state triggered a cuddle.");
        }
        hold.Begin(6000, true);
        hold.Cancel();
        Require(!hold.TryTrigger(6600, true, true, true),
            "A cancelled hold triggered after deactivation or hiding.");

        var taps = new PetTapCooldown();
        var now = DateTimeOffset.UnixEpoch;
        Require(taps.TryAccept(now) && !taps.TryAccept(now.AddMilliseconds(699))
            && taps.TryAccept(now.AddMilliseconds(700)),
            "Cloud tap cooldown allowed overlapping responses or blocked the next response.");
        Console.WriteLine("PASS: 550 ms one-shot cuddle, drag/double-click cancellation and cloud tap cooldown.");
    }

    private static void CheckArtwork()
    {
        var library = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory, "assets", "character", "animations"));
        var posters = new Dictionary<string, string>();
        var standing = library.Load("Greet")?.Frames[0]
            ?? throw new InvalidOperationException("Missing neutral animation pose.");
        var standingPixels = CopyPixels(standing);
        foreach (var id in Enum.GetNames<PetActivity>().Concat(["Blink", "Hover", "Tail", "Sleep", "SleepBreath"]))
        {
            var loaded = library.Load(id);
            Require(loaded is not null, $"Missing or damaged animation artwork: {id}");
            Require(loaded!.Frames.All(f => f.PixelWidth == 512 && f.PixelHeight == 512),
                $"Misaligned canvas: {id}");
            foreach (var (frame, index) in loaded.Frames.Select((frame, index) => (frame, index)))
            {
                var pixels = CopyPixels(frame);
                var minX = 512; var minY = 512; var maxX = -1; var maxY = -1; var foot = -1;
                for (var y = 0; y < 512; y++)
                    for (var x = 0; x < 512; x++)
                    {
                        if (pixels[(y * 512 + x) * 4 + 3] < 96) continue;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                        if (y >= 400 && x is >= 170 and < 342) foot = y;
                    }
                Require(minX >= 10 && maxX <= 500 && minY >= 3 && maxY <= 500,
                    $"Clipped or marginless character in {id}/{index}: {minX},{minY}–{maxX},{maxY}");
                foreach (var (left, right) in new[] { (45, 180), (332, 467) })
                {
                    var visibleWingPixels = 0;
                    for (var y = 245; y < 390; y++)
                        for (var x = left; x < right; x++)
                            if (pixels[(y * 512 + x) * 4 + 3] >= 96) visibleWingPixels++;
                    Require(visibleWingPixels > 2500,
                        $"Wing or adjoining silhouette is missing in {id}/{index} at x={left}..{right}.");
                }
                if (id != "Hop") Require(foot is >= 490 and <= 500,
                    $"Foot anchor drifted in {id}/{index}: {foot}");
                if (id == "Land" && index is >= 1 and <= 3)
                {
                    // The previous broad pose mask left a second face above
                    // the crouching face. Its skin pixels occupied this band.
                    var upperFacePixels = 0;
                    for (var y = 190; y < 216; y++)
                        for (var x = 175; x < 345; x++)
                        {
                            var offset = (y * 512 + x) * 4;
                            var blue = pixels[offset]; var green = pixels[offset + 1];
                            var red = pixels[offset + 2]; var alpha = pixels[offset + 3];
                            if (alpha > 128 && red > 190 && green > 120
                                && red > green + 15 && green > blue + 5) upperFacePixels++;
                        }
                    Require(upperFacePixels < 100,
                        $"The old standing face overlaps the Land/{index} crouch.");
                }
                if (id is not ("Sleep" or "SleepBreath" or "Hover")
                    && (index == 5 || index == 0 && id != "Wake"))
                    Require(pixels.AsSpan().SequenceEqual(standingPixels),
                        $"{id} does not enter/leave on the fixed standing pose.");
            }
            var buffer = CopyPixels(loaded.Frames[loaded.Clip.PosterFrame]);
            Require(buffer[3] == 0 && buffer[^1] == 0,
                $"Nontransparent outer corner or damaged canvas: {id}");
            posters[id] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer));
        }
        var actions = Enum.GetNames<PetActivity>();
        Require(CopyPixels(library.Load("Wake")!.Frames[0]).AsSpan()
                .SequenceEqual(CopyPixels(library.Load("Sleep")!.Frames[0])),
            "Waking starts on a different image than the sleeping state.");
        Require(CopyPixels(library.Load("Sleep")!.Frames[0]).AsSpan()
                .SequenceEqual(CopyPixels(library.Load("Sleep")!.Frames[5])),
            "Sleep loop does not return to its closed-eye resting pose.");
        Require(library.Load("Sleep")!.Frames.Skip(1).All(frame =>
                CopyPixels(frame).AsSpan().SequenceEqual(CopyPixels(library.Load("Sleep")!.Frames[0]))),
            "Sleeping should keep the curled pose instead of occasionally rocking through standing frames.");
        Require(!CopyPixels(library.Load("Sleep")!.Frames[0]).AsSpan().SequenceEqual(standingPixels),
            "Sleeping still reuses a standing drawing.");
        Require(actions.Select(id => posters[id]).Distinct().Count() == actions.Length,
            "Two interactions share the same primary pose.");
        var hover = library.Load("Hover")!;
        Require(CopyPixels(hover.Frames[0]).AsSpan().SequenceEqual(standingPixels),
            "Hover must begin at the idle drawing without a pose flash.");
        Require(!CopyPixels(hover.Frames[^1]).AsSpan().SequenceEqual(standingPixels),
            "Hover repeats the idle pose instead of looking toward the pointer.");
        foreach (var id in new[] { "Greet", "Pet", "Feed", "Cuddle", "Land" })
        {
            var action = CopyPixels(library.Load(id)!.Frames[1]);
            var changedOuterPixels = 0;
            for (var y = 220; y < 395; y++)
                for (var x = 0; x < 512; x++)
                {
                    if (x is >= 180 and < 330) continue;
                    var offset = (y * 512 + x) * 4;
                    if (!action.AsSpan(offset, 4).SequenceEqual(standingPixels.AsSpan(offset, 4)))
                        changedOuterPixels++;
                }
            Require(changedOuterPixels > 1000,
                $"{id} still has idle wings/hair/tail pasted onto its active pose.");
        }
        foreach (var id in new[] { "Feed", "Stretch" })
        {
            var centers = new List<double>();
            foreach (var frame in library.Load(id)!.Frames.Skip(1).Take(4))
            {
                var pixels = CopyPixels(frame);
                var left = 512; var right = -1;
                for (var y = 235; y < 380; y++)
                    for (var x = 0; x < 512; x++)
                        if (pixels[(y * 512 + x) * 4 + 3] >= 96)
                        { left = Math.Min(left, x); right = Math.Max(right, x); }
                centers.Add((left + right) / 2.0);
            }
            Require(centers.Max() - centers.Min() <= 5,
                $"{id} still alternates between left and right wing silhouettes.");
        }
        Console.WriteLine("PASS: complete wing margins, whole-character action poses, foot anchors and distinct hover.");
    }

    private static byte[] CopyPixels(BitmapSource frame)
    {
        var pixels = new byte[512 * 512 * 4];
        frame.CopyPixels(pixels, 512 * 4, 0);
        return pixels;
    }

    private static void CheckCompanionArtwork(string directory)
    {
        var spriteDirectory = Path.Combine(AssetService.CharacterDirectory, "companion");
        var names = new[] { "normal", "blink", "curious", "happy", "thinking", "angry" };
        byte[]? normalMask = null;
        foreach (var name in names)
        {
            var image = new BitmapImage(new Uri(Path.Combine(spriteDirectory, name + ".png")));
            Require(image.PixelWidth == 1254 && image.PixelHeight == 1254,
                $"The {name} companion changed canvas dimensions.");
            var crop = new CroppedBitmap(image, new Int32Rect(365, 435, 555, 437));
            var pixels = new byte[crop.PixelWidth * crop.PixelHeight * 4];
            crop.CopyPixels(pixels, crop.PixelWidth * 4, 0);
            var mask = new byte[crop.PixelWidth * crop.PixelHeight];
            var count = 0;
            for (var i = 0; i < mask.Length; i++)
            {
                mask[i] = pixels[i * 4 + 3] >= 128 ? (byte)1 : (byte)0;
                count += mask[i];
            }
            Require(count > mask.Length / 2 && count < mask.Length * 9 / 10,
                $"The {name} companion is missing or has no transparent margin.");
            if (normalMask is null) normalMask = mask;
            else
            {
                var changed = 0;
                for (var i = 0; i < mask.Length; i++)
                    if (mask[i] != normalMask[i]) changed++;
                Require(changed < count / 50,
                    $"The {name} companion silhouette jumps between expressions.");
            }
        }
        var fallbackImage = new WpfImage();
        var fallbackAnimator = new PetCompanionAnimator(fallbackImage,
            new ScaleTransform(), new RotateTransform(), new TranslateTransform());
        var idle = new BitmapImage(new Uri(AssetService.CharacterPath));
        var brokenDirectory = Path.Combine(directory, "missing-companion");
        Directory.CreateDirectory(brokenDirectory);
        File.WriteAllText(Path.Combine(brokenDirectory, "normal.png"), "not a PNG");
        fallbackAnimator.LoadSources(idle, idle, idle, idle, idle, brokenDirectory);
        fallbackAnimator.SetState(PetState.Idle, true, false);
        Require(fallbackImage.Source is not null,
            "A missing or broken companion sprite did not fall back to original state art.");
        Console.WriteLine("PASS: six clean aligned cloud expressions and damaged-asset fallback.");
    }

    private static void RenderUi(string directory)
    {
        // Instantiate resources without App.Run/Window.Show: no tray, single-instance signal or user data.
        var app = new App(); app.InitializeComponent();
        var store = new ProductivityStore(Path.Combine(directory, "isolated-data"));
        typeof(App).GetProperty(nameof(App.PomodoroService))!.SetValue(app, new PomodoroService(store, () => app.Settings));
        var window = new MainWindow(app);
        foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (field.GetValue(window) is DispatcherTimer timer) timer.Stop();
        Invoke(window, "LoadCharacterAssets");
        var characterImage = (WpfImage)window.FindName("CharacterImage");
        var companion = (WpfImage)window.FindName("AiCompanion");
        var companionHitArea = (Border)window.FindName("AiCompanionHitArea");
        // Measure the cloud body in dragged.png (about x=925..1045, y=375..473).
        // The state image fits the 168-wide host; match both size and center.
        var stateScale = 168d / 1241d;
        var referenceWidth = 120 * stateScale;
        var referenceHeight = 98 * stateScale;
        var referenceCenterX = 985 * stateScale;
        var referenceCenterY = (220 - 1268 * stateScale) / 2 + 424 * stateScale;
        Require(companion.Width >= referenceWidth * .9 && companion.Width <= referenceWidth * 1.2
            && companion.Height >= referenceHeight * .9 && companion.Height <= referenceHeight * 1.2
            && Math.Abs(168 - companion.Margin.Right - companion.Width / 2 - referenceCenterX) < 3
            && Math.Abs(companion.Margin.Top + companion.Height / 2 - referenceCenterY) < 3,
            "The independent small AI no longer matches the cloud in the dragged state.");
        Require(companion.Source is not null, "The small AI companion was lost from animation poses.");
        Require(companionHitArea.Width == 28 && companionHitArea.Height == 26
            && companion.Width == 18 && companion.Height == 15
            && Math.Abs((168 - companionHitArea.Margin.Right - companionHitArea.Width / 2)
                - (168 - companion.Margin.Right - companion.Width / 2)) < 1
            && Math.Abs(companionHitArea.Margin.Top + companionHitArea.Height / 2
                - companion.Margin.Top - companion.Height / 2) < 1,
            "The transparent cloud hit target changed the sprite size or missed its center.");
        var normalCompanion = companion.Source;
        Invoke(window, "ApplyStateVisual", PetState.Hover);
        Require(companion.Visibility == Visibility.Visible, "The companion is hidden on hover.");
        Require(!ReferenceEquals(companion.Source, normalCompanion),
            "The companion still uses one expression for idle and curiosity.");
        var companionAnimator = (PetCompanionAnimator)typeof(MainWindow)
            .GetField("_companionAnimator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        companionAnimator.React(PetActivity.Celebrate);
        var companionMotion = (TranslateTransform)window.FindName("AiCompanionTranslate");
        Require(!ReferenceEquals(companion.Source, normalCompanion) && companionMotion.HasAnimatedProperties
            && !((TranslateTransform)window.FindName("PetTranslateTransform")).HasAnimatedProperties,
            "Celebration did not animate the small AI independently from the pet.");
        companionAnimator.SetState(PetState.Idle, false, false);
        typeof(PetCompanionAnimator).GetField("_nextAmbient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(companionAnimator, DateTimeOffset.UtcNow.AddSeconds(-1));
        companionAnimator.Tick(DateTimeOffset.UtcNow, false);
        Require(companionMotion.HasAnimatedProperties
            && !ReferenceEquals(companion.Source, normalCompanion)
            && !((ScaleTransform)window.FindName("AiCompanionScale")).HasAnimatedProperties,
            "The small AI does not use its own closed-eye frame and float while idle.");
        var blinkTimer = (DispatcherTimer)typeof(PetCompanionAnimator)
            .GetField("_blinkTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        Require(blinkTimer.IsEnabled && blinkTimer.Interval == TimeSpan.FromMilliseconds(170),
            "The small AI blink is not a short one-shot action.");
        typeof(PetCompanionAnimator).GetMethod("EndBlink", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(companionAnimator, [null, EventArgs.Empty]);
        Require(ReferenceEquals(companion.Source, normalCompanion),
            "The small AI stayed closed-eyed after its one-shot blink.");
        companionAnimator.SetState(PetState.Idle, true, false);
        companionAnimator.React(PetActivity.Feed);
        Require(!companionMotion.HasAnimatedProperties
            && !((ScaleTransform)window.FindName("AiCompanionScale")).HasAnimatedProperties
            && !ReferenceEquals(companion.Source, normalCompanion),
            "Reduced motion did not keep a static but expressive companion.");
        companionAnimator.SetState(PetState.Idle, false, false);
        companionAnimator.Tap();
        var tapTimer = (DispatcherTimer)typeof(PetCompanionAnimator)
            .GetField("_tapTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        Require(tapTimer.IsEnabled && tapTimer.Interval == TimeSpan.FromMilliseconds(700)
            && companionMotion.HasAnimatedProperties && !ReferenceEquals(companion.Source, normalCompanion)
            && !((TranslateTransform)window.FindName("PetTranslateTransform")).HasAnimatedProperties,
            "Cloud tap did not respond independently for 700 ms.");
        var tapFace = companion.Source;
        typeof(PetCompanionAnimator).GetMethod("EndBlink", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(companionAnimator, [null, EventArgs.Empty]);
        Require(ReferenceEquals(companion.Source, tapFace),
            "An old blink callback replaced the cloud tap expression.");
        companionAnimator.SetState(PetState.Sleeping, false, false);
        Require(!tapTimer.IsEnabled && companion.Visibility == Visibility.Collapsed
            && companionHitArea.Visibility == Visibility.Collapsed,
            "The cloud tap continued after sleep.");
        companionAnimator.SetState(PetState.Idle, true, false);
        companionAnimator.Tap();
        var staticTapTimer = (DispatcherTimer)typeof(PetCompanionAnimator)
            .GetField("_tapTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        Require(staticTapTimer.IsEnabled && !companionMotion.HasAnimatedProperties
            && !ReferenceEquals(companion.Source, normalCompanion),
            "Reduced motion did not use a static cloud tap expression.");
        companionAnimator.React(PetActivity.Greet);
        Require(!staticTapTimer.IsEnabled,
            "A new pet interaction did not cancel the old cloud response.");
        companionAnimator.Suspend();
        Require(companion.Visibility == Visibility.Collapsed,
            "The cloud stayed visible after hiding.");
        companionAnimator.SetState(PetState.Idle, false, false);
        companionAnimator.Tap();
        var olderTap = (DispatcherTimer)typeof(PetCompanionAnimator)
            .GetField("_tapTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        var olderRevision = (long)typeof(PetCompanionAnimator)
            .GetField("_revision", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        companionAnimator.Tap();
        var currentTap = (DispatcherTimer)typeof(PetCompanionAnimator)
            .GetField("_tapTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        var currentRevision = (long)typeof(PetCompanionAnimator)
            .GetField("_revision", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(companionAnimator)!;
        typeof(PetCompanionAnimator).GetMethod("EndTap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(companionAnimator, [olderTap, olderRevision]);
        Require(currentTap.IsEnabled && !ReferenceEquals(companion.Source, normalCompanion),
            "The older cloud response ended a newer tap early.");
        typeof(PetCompanionAnimator).GetMethod("EndTap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(companionAnimator, [currentTap, currentRevision]);
        Require(ReferenceEquals(companion.Source, normalCompanion)
            && !companionMotion.HasAnimatedProperties,
            "The cloud did not return to its calm face after the response.");
        Require(!CopyPixels((BitmapSource)characterImage.Source).AsSpan()
                .SequenceEqual(CopyPixels(new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory,
                    "assets", "character", "animations")).Load("Greet")!.Frames[0])),
            "The actual hover state still displays the idle character image.");
        Invoke(window, "ApplyStateVisual", PetState.Sleeping);
        Require(companion.Visibility == Visibility.Collapsed,
            "The awake companion is duplicated over the original sleeping companion.");
        Require(!companionMotion.HasAnimatedProperties,
            "The small AI kept floating after the pet fell asleep.");
        Require(CopyPixels((BitmapSource)characterImage.Source).AsSpan()
                .SequenceEqual(CopyPixels(new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory,
                    "assets", "character", "animations")).Load("Sleep")!.Frames[0])),
            "The actual sleeping state does not show the curled sleep drawing.");
        var player = (PetAnimationPlayer)typeof(MainWindow)
            .GetField("_animationPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        typeof(MainWindow).GetField("_activePetActivity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, PetActivity.Wake);
        var wake = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory,
            "assets", "character", "animations")).Load("Wake")!;
        player.Play(wake, false);
        Require(companion.Visibility == Visibility.Collapsed,
            "The waking first frame duplicates the companion already in the sleep artwork.");
        typeof(PetAnimationPlayer).GetMethod("Advance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, [wake.Clip.DurationsMs[0] + 1L]);
        Require(companion.Visibility == Visibility.Visible,
            "The companion did not return when the pet rose from sleep.");
        player.Stop();
        typeof(MainWindow).GetField("_activePetActivity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, null);
        Invoke(window, "PlayQuietClip", "Blink");
        Invoke(window, "PlayQuietClip", "Tail");
        var running = (LoadedPetAnimation?)typeof(PetAnimationPlayer)
            .GetField("_animation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player);
        Require(player.IsPlaying && running?.Clip.Id == "Blink",
            "A second quiet animation interrupted the first.");
        player.Stop();
        typeof(MainWindow).GetField("_activePetActivity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, PetActivity.Greet);
        Invoke(window, "PlayQuietClip", "Blink");
        Require(!player.IsPlaying, "Idle blink started during an interaction.");
        typeof(MainWindow).GetField("_activePetActivity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, null);
        typeof(MainWindow).GetField("_feeding", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        ((Canvas)window.FindName("TreatLayer")).Visibility = Visibility.Visible;
        Invoke(window, "CancelTreat");
        Require(!(bool)typeof(MainWindow).GetField("_feeding", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!
            && ((Canvas)window.FindName("TreatLayer")).Visibility == Visibility.Collapsed,
            "Cancelled feeding still leaves the treat visible or active.");
        var root = (Grid)window.Content;
        root.Background = new SolidColorBrush(Color.FromRgb(239, 234, 248));
        var menu = (ContextMenu)window.FindResource("InteractionMenu");
        menu.ApplyTemplate();
        Require(menu.Items.OfType<MenuItem>().Count() == 8, "Expected seven play actions plus rest/wake.");
        menu.Measure(new System.Windows.Size(192, double.PositiveInfinity));
        var menuHeight = (int)Math.Ceiling(menu.DesiredSize.Height);
        Save(menu, 192, menuHeight, Path.Combine(directory, "interaction-menu.png"));
        var lastItem = menu.Items.OfType<MenuItem>().Last();
        Require(lastItem.TranslatePoint(new System.Windows.Point(), menu).Y + lastItem.ActualHeight <= menuHeight,
            "Rest/wake menu item is clipped.");

        ((TextBlock)window.FindName("StateText")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)VisualTreeHelper.GetParent((TextBlock)window.FindName("StateText")))
            .Visibility = Visibility.Collapsed;
        ((TextBlock)window.FindName("PetAccentText")).Visibility = Visibility.Collapsed;
        var artwork = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory,
            "assets", "character", "animations"));
        foreach (var scaleValue in new[] { .6, 1, 2 })
        {
            Invoke(window, "ApplyScale", scaleValue, false);
            Invoke(window, "ApplyStateVisual", PetState.Idle);
            root.Measure(new System.Windows.Size(830, 590));
            root.Arrange(new Rect(0, 0, 830, 590)); root.UpdateLayout();
            var hitBounds = companionHitArea.TransformToAncestor(root)
                .TransformBounds(new Rect(companionHitArea.RenderSize));
            Require(companionHitArea.Visibility == Visibility.Visible
                && Math.Abs(hitBounds.Width - 28 * scaleValue) < 1
                && Math.Abs(hitBounds.Height - 26 * scaleValue) < 1
                && hitBounds.Left >= 0 && hitBounds.Top >= 0
                && hitBounds.Right <= 830 && hitBounds.Bottom <= 590,
                $"The cloud hit target is missing, off-center or clipped at {scaleValue:P0}.");
            Require(ReferenceEquals(VisualTreeHelper.HitTest(root,
                new System.Windows.Point(hitBounds.Left + hitBounds.Width / 2,
                    hitBounds.Top + hitBounds.Height / 2))?.VisualHit, companionHitArea),
                $"The cloud hit target is covered at {scaleValue:P0}.");
            Invoke(window, "ApplyStateVisual", PetState.Dragged);
            Save(root, 830, 590, Path.Combine(directory, $"dragged-scale-{scaleValue:0.0}.png"));
            typeof(MainWindow).GetField("_preserveCharacterImageForAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, true);
            Invoke(window, "ApplyStateVisual", PetState.Happy);
            typeof(MainWindow).GetField("_preserveCharacterImageForAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, false);
            companionAnimator.SetState(PetState.Happy, true, true);
            companionAnimator.React(PetActivity.Greet);
            Require(companion.Visibility == Visibility.Visible,
                "The companion is missing while interaction artwork plays.");
            foreach (var id in Enum.GetNames<PetActivity>())
            {
                var animation = artwork.Load(id)!;
                foreach (var frame in animation.Frames)
                {
                    ((WpfImage)window.FindName("CharacterImage")).Source = frame;
                    root.Measure(new System.Windows.Size(830, 590));
                    root.Arrange(new Rect(0, 0, 830, 590)); root.UpdateLayout();
                    var characterBounds = ((Grid)window.FindName("CharacterHost"))
                        .TransformToAncestor(root).TransformBounds(
                            new Rect(((Grid)window.FindName("CharacterHost")).RenderSize));
                    Require(characterBounds.Left >= 0 && characterBounds.Top >= 0
                        && characterBounds.Right <= 830 && characterBounds.Bottom <= 590,
                        $"{id} frame escaped the window at {scaleValue:P0}.");
                }
            }
            var jump = (TranslateTransform)window.FindName("PetTranslateTransform");
            jump.Y = -48 * scaleValue;
            ((WpfImage)window.FindName("CharacterImage")).Source = artwork.Load("Hop")!.Frames[2];
            root.Measure(new System.Windows.Size(830, 590));
            root.Arrange(new Rect(0, 0, 830, 590)); root.UpdateLayout();
            var jumpBounds = ((Grid)window.FindName("CharacterHost"))
                .TransformToAncestor(root).TransformBounds(
                    new Rect(((Grid)window.FindName("CharacterHost")).RenderSize));
            Require(jumpBounds.Top >= 0 && jumpBounds.Bottom <= 590,
                $"The real hop apex is clipped at {scaleValue:P0}.");
            if (scaleValue == 2)
                Save(root, 830, 590, Path.Combine(directory, "hop-apex-scale-2.0.png"));
            jump.Y = 0;
            ((WpfImage)window.FindName("CharacterImage")).Source = artwork.Load("Greet")!.Frames[3];
            Save(root, 830, 590, Path.Combine(directory, $"greet-scale-{scaleValue:0.0}.png"));
            ((WpfImage)window.FindName("CharacterImage")).Source = artwork.Load("Celebrate")!.Frames[2];
            Save(root, 830, 590, Path.Combine(directory, $"celebrate-scale-{scaleValue:0.0}.png"));
            if (scaleValue == 2)
                foreach (var id in new[] { "Land", "Dance", "Stretch", "Feed", "Sleep" })
                {
                    if (id == "Sleep") Invoke(window, "ApplyStateVisual", PetState.Sleeping);
                    ((WpfImage)window.FindName("CharacterImage")).Source = artwork.Load(id)!.Frames[2];
                    Save(root, 830, 590, Path.Combine(directory, $"{id.ToLowerInvariant()}-scale-2.0.png"));
                }
            ((WpfImage)window.FindName("CharacterImage")).Source = artwork.Load("Greet")!.Frames[0];
            var hitArgs = new object[] { Rect.Empty };
            Require((bool)typeof(MainWindow).GetMethod("TryGetArtworkRect", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, hitArgs)!, "No character artwork at supported scale.");
            var art = (Rect)hitArgs[0];
            var head = new System.Windows.Point(art.X + art.Width * .5, art.Y + art.Height * .27);
            Require((bool)typeof(MainWindow).GetMethod("IsCharacterPixelHit", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [head])!, "Head hit region missed the character at supported scale.");
        }
        var settings = new SettingsWindow(app.Settings, _ => { });
        var settingsRoot = (Grid)settings.Content;
        settingsRoot.Background = settings.Background;
        Save(settingsRoot, 548, 700, Path.Combine(directory, "settings-top.png"));
        var preference = (System.Windows.Controls.CheckBox)settings.FindName("AmbientPetActionsBox");
        DependencyObject? ancestor = preference;
        while (ancestor is not null && ancestor is not ScrollViewer) ancestor = VisualTreeHelper.GetParent(ancestor);
        var scroll = (ScrollViewer)ancestor!;
        scroll.ScrollToVerticalOffset(390);
        Save(settingsRoot, 548, 700, Path.Combine(directory, "interaction-settings.png"));
        var preferencePosition = preference.TranslatePoint(new System.Windows.Point(), settingsRoot);
        Require(preferencePosition.Y > 100 && preferencePosition.Y + preference.ActualHeight < 620,
            "Interaction preference is not accessible in the settings scroll area.");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Console.WriteLine("PASS: rendered actual WPF menu/settings and character at 60%, 100%, 200% without showing a window.");
    }

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void Save(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
