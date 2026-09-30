using System.Reflection;
using System.Runtime.CompilerServices;
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
    public static int RunVariants(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                CheckVariantArtwork();
                CheckVariantPlayback();
                CheckCompanionVariantReactions();
                RenderVariantChoices(directory);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) { Console.WriteLine(failure); return 1; }
        return 0;
    }

    private static void CheckVariantArtwork()
    {
        var library = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory, "assets", "character", "animations"));
        var snackPosters = new List<byte[]>();
        foreach (var snack in PetInteractionVariants.Snacks)
        {
            var id = PetInteractionVariants.ClipId(snack);
            var animation = library.Load(id) ?? throw new InvalidOperationException($"Missing {id}");
            Require(animation.Clip.DurationMs is >= 1400 and <= 2200 && animation.Clip.IsValid,
                $"Invalid feeding timing: {id}");
            CheckVariantFrameGeometry(id, animation);
            snackPosters.Add(CopyPixels(animation.Frames[animation.Clip.PosterFrame]));
        }
        for (var i = 0; i < snackPosters.Count; i++)
            for (var j = i + 1; j < snackPosters.Count; j++)
                Require(!snackPosters[i].SequenceEqual(snackPosters[j]),
                    "Two snacks share the same reaction poster.");
        var dancePosters = new List<byte[]>();
        foreach (var dance in PetInteractionVariants.Dances)
        {
            var id = PetInteractionVariants.ClipId(dance);
            var animation = library.Load(id) ?? throw new InvalidOperationException($"Missing {id}");
            Require(animation.Clip.DurationMs == 8000 && animation.Clip.Frames.Length is >= 20 and <= 32
                && animation.Clip.DurationsMs.Distinct().Count() >= 3,
                $"Dance must be a finite eight-second phrase: {id}");
            Require(animation.Frames.Distinct().Count() >= 5,
                $"Dance uses too few different poses: {id}");
            CheckVariantFrameGeometry(id, animation);
            CheckDanceContinuity(id, animation);
            dancePosters.Add(CopyPixels(animation.Frames[animation.Clip.PosterFrame]));
        }
        for (var i = 0; i < dancePosters.Count; i++)
            for (var j = i + 1; j < dancePosters.Count; j++)
                Require(!dancePosters[i].SequenceEqual(dancePosters[j]),
                    "Two dances share the same representative pose.");
        Console.WriteLine("PASS: five distinct snacks, three distinct finite 8-second dances, full-frame margins and stable feet.");
    }

    private static void CheckDanceContinuity(string id, LoadedPetAnimation animation)
    {
        var pixels = animation.Frames.Distinct().ToDictionary(frame => frame, frame =>
        {
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            return CopyPixels(converted);
        });
        var abrupt = 0;
        for (var i = 1; i < animation.Frames.Length; i++)
        {
            var before = animation.Frames[i - 1]; var after = animation.Frames[i];
            if (ReferenceEquals(before, after)) continue;
            var a = pixels[before]; var b = pixels[after];
            long difference = 0; var samples = 0;
            for (var y = 0; y < 512; y += 4)
                for (var x = 0; x < 512; x += 4)
                {
                    var offset = (y * 512 + x) * 4;
                    for (var channel = 0; channel < 4; channel++)
                        difference += Math.Abs(a[offset + channel] - b[offset + channel]);
                    samples++;
                }
            var score = 100d * difference / (samples * 4 * 255);
            if (score > 12) abrupt++;
        }
        Require(abrupt <= (id == "DanceWingTail" ? 2 : 0),
            $"Abrupt unrelated pose jump in {id}: {abrupt}");
    }

    private static void CheckVariantFrameGeometry(string id, LoadedPetAnimation animation)
    {
        foreach (var frame in animation.Frames.Distinct())
        {
            Require(frame.PixelWidth == 512 && frame.PixelHeight == 512, $"Wrong canvas: {id}");
            var pixels = CopyPixels(frame);
            var minX = 512; var minY = 512; var maxX = -1; var maxY = -1; var foot = -1;
            var leftWing = 0; var rightWing = 0; var body = 0;
            for (var y = 0; y < 512; y++)
                for (var x = 0; x < 512; x++)
                {
                    if (pixels[(y * 512 + x) * 4 + 3] < 96) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    if (x is >= 220 and <= 290 && y > 460) foot = Math.Max(foot, y);
                    if (y is >= 210 and <= 370)
                    {
                        if (x < 155) leftWing++;
                        else if (x > 357) rightWing++;
                        else body++;
                    }
                }
            Require(minX >= 4 && minY >= 4 && maxX <= 507 && maxY <= 507,
                $"Cropped variant silhouette: {id}, {(minX, minY, maxX, maxY)}");
            Require(foot is >= 490 and <= 502, $"Unstable foot anchor: {id}, {foot}");
            Require(leftWing > 350 && rightWing > 350 && body > 2000,
                $"Wing or body is missing in {id}: {leftWing}/{rightWing}/{body}");
        }
    }

    private static void CheckVariantPlayback()
    {
        var library = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory, "assets", "character", "animations"));
        var first = library.Load("DanceStep")!;
        var second = library.Load("DanceWingTail")!;
        using var player = new PetAnimationPlayer();
        var oldCompletion = 0;
        var newCompletion = 0;
        BitmapSource? shown = null;
        player.FrameChanged += frame => shown = frame;
        player.Play(first, false, () => oldCompletion++);
        Require(player.IsPlaying && ReferenceEquals(shown, first.Frames[0]),
            "The first selected dance did not start on its own frame.");
        player.Play(second, false, () => newCompletion++);
        typeof(PetAnimationPlayer).GetMethod("Advance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, [2250L]);
        Require(player.IsPlaying && ReferenceEquals(shown, second.Frames[second.Clip.FrameAt(2250)])
            && oldCompletion == 0, "A previous dance survived interruption.");
        typeof(PetAnimationPlayer).GetMethod("Advance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, [(long)second.Clip.DurationMs]);
        Require(!player.IsPlaying && oldCompletion == 0 && newCompletion == 1,
            "The selected dance did not end exactly once.");
        player.Play(first, true, () => oldCompletion++);
        Require(!player.IsPlaying && ReferenceEquals(shown, first.Frames[first.Clip.PosterFrame])
            && oldCompletion == 0, "Reduced motion did not hold a static dance pose.");
        player.Stop();
        Require(!player.IsPlaying && player.CurrentFrameIndex == -1,
            "Hiding did not stop a selected variant.");
        Console.WriteLine("PASS: selected dance replacement, one-shot completion, static mode and stop.");
    }

    private static void CheckCompanionVariantReactions()
    {
        var image = new WpfImage { Width = 18, Height = 15 };
        var scale = new ScaleTransform(); var rotate = new RotateTransform(); var translate = new TranslateTransform();
        var animator = new PetCompanionAnimator(image, scale, rotate, translate);
        var original = new BitmapImage(new Uri(Path.Combine(AssetService.CharacterDirectory, "default.png")));
        animator.LoadSources(original, original, original, original, original);
        var type = typeof(PetCompanionAnimator);
        var moodType = type.GetNestedType("Mood", BindingFlags.NonPublic)!;
        BitmapSource? Face(string name) => (BitmapSource?)type.GetMethod("Face", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(animator, [Enum.Parse(moodType, name)]);
        object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(animator)!;
        void Advance(DispatcherTimer timer, long revision, long elapsed) =>
            type.GetMethod("AdvanceReaction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(animator, [timer, revision, elapsed]);
        var signatures = new HashSet<string>();
        foreach (var snack in PetInteractionVariants.Snacks)
        {
            animator.SetState(PetState.Happy, false, true);
            animator.ReactSnack(snack, 1630);
            var timer = (DispatcherTimer)Field("_reactionTimer");
            var revision = (long)Field("_revision");
            var faces = ((Array)Field("_reactionFaces")).Cast<ITuple>()
                .Select(cue => $"{cue[0]}:{cue[1]}");
            var motion = ((Array)Field("_reactionMotion")).Cast<ITuple>()
                .Select(cue => $"{cue[0]}:{cue[1]}").ToArray();
            Require(signatures.Add(string.Join("/", faces) + "|" + string.Join("/", motion)),
                $"The {snack} cloud reaction repeats another snack.");
            Require(timer.IsEnabled && (int)Field("_reactionDurationMs") == 1630
                && translate.HasAnimatedProperties && !rotate.HasAnimatedProperties && !scale.HasAnimatedProperties
                && ((Array)Field("_reactionMotion")).Cast<ITuple>()
                    .All(cue => Math.Abs((double)cue[1]!) <= 3),
                $"The {snack} cloud reaction changes size, rotates, or exceeds its small vertical range.");
            Advance(timer, revision, 1630);
            Require(!timer.IsEnabled && ReferenceEquals(image.Source, Face("Normal"))
                && !translate.HasAnimatedProperties, $"The {snack} reaction did not settle.");
        }
        foreach (var dance in PetInteractionVariants.Dances)
        {
            animator.SetState(PetState.Happy, false, true);
            animator.ReactDance(dance, 8000);
            var timer = (DispatcherTimer)Field("_reactionTimer");
            var revision = (long)Field("_revision");
            var faces = ((Array)Field("_reactionFaces")).Cast<ITuple>()
                .Select(cue => $"{cue[0]}:{cue[1]}");
            var motion = ((Array)Field("_reactionMotion")).Cast<ITuple>()
                .Select(cue => $"{cue[0]}:{cue[1]}").ToArray();
            Require(signatures.Add(string.Join("/", faces) + "|" + string.Join("/", motion)),
                $"The {dance} cloud dance repeats another reaction.");
            Require(timer.IsEnabled && (int)Field("_reactionDurationMs") == 8000
                && translate.HasAnimatedProperties && !rotate.HasAnimatedProperties && !scale.HasAnimatedProperties
                && ((Array)Field("_reactionMotion")).Cast<ITuple>()
                    .All(cue => Math.Abs((double)cue[1]!) <= 3),
                $"The {dance} cloud dance changes size, rotates, or exceeds its small vertical range.");
            if (dance == PetDance.WingTail)
            {
                Require(ReferenceEquals(image.Source, Face("Curious")), "Wing-tail cloud did not begin curious.");
                Advance(timer, revision, 5859);
                Require(ReferenceEquals(image.Source, Face("Curious")), "Wing-tail cloud anticipated the wing opening.");
                Advance(timer, revision, 5860);
                Require(ReferenceEquals(image.Source, Face("Happy")), "Wing-tail cloud missed the wing opening.");
            }
            Advance(timer, revision, 8000);
            Require(!timer.IsEnabled && ReferenceEquals(image.Source, Face("Normal"))
                && !translate.HasAnimatedProperties, $"The {dance} cloud dance did not settle.");
        }
        animator.SetState(PetState.Happy, false, true);
        animator.ReactDance(PetDance.Step, 8000);
        var oldTimer = (DispatcherTimer)Field("_reactionTimer");
        var oldRevision = (long)Field("_revision");
        animator.ReactDance(PetDance.WingTail, 8000);
        var currentTimer = (DispatcherTimer)Field("_reactionTimer");
        var currentRevision = (long)Field("_revision");
        Advance(oldTimer, oldRevision, 8000);
        Require(!oldTimer.IsEnabled && currentTimer.IsEnabled
            && ReferenceEquals(image.Source, Face("Curious")),
            "An interrupted companion callback replaced the new dance.");
        animator.Tap();
        var tapTimer = (DispatcherTimer)Field("_tapTimer");
        Require(!currentTimer.IsEnabled && tapTimer.IsEnabled
            && ReferenceEquals(image.Source, Face("Happy")),
            "Tapping the cloud did not interrupt its dance independently.");
        Advance(currentTimer, currentRevision, 8000);
        Require(tapTimer.IsEnabled && ReferenceEquals(image.Source, Face("Happy")),
            "An old dance callback overrode the cloud tap.");
        animator.SetState(PetState.Sleeping, false, false);
        Require(!tapTimer.IsEnabled && image.Visibility == Visibility.Collapsed,
            "The cloud remained visible over sleeping artwork.");
        animator.SetState(PetState.Happy, true, true);
        animator.ReactSnack(PetSnack.Strawberry, 1630);
        Require(type.GetField("_reactionTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(animator) is null
            && !translate.HasAnimatedProperties && ReferenceEquals(image.Source, Face("Curious")),
            "Reduced motion did not keep a static snack expression.");
        animator.SetState(PetState.Happy, true, true);
        animator.ReactDance(PetDance.Guofeng, 8000);
        Require(type.GetField("_reactionTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(animator) is null
            && !translate.HasAnimatedProperties && ReferenceEquals(image.Source, Face("Thinking")),
            "Reduced motion did not keep a static companion expression.");
        animator.Suspend();
        Require(image.Visibility == Visibility.Collapsed,
            "Hiding did not stop the companion reaction.");
        Console.WriteLine("PASS: eight distinct companion reactions, wing sync, interruption, sleep and static mode.");
    }

    private static void RenderVariantChoices(string directory)
    {
        var app = new App(); app.InitializeComponent();
        var store = new ProductivityStore(Path.Combine(directory, "isolated-data"));
        typeof(App).GetProperty(nameof(App.PomodoroService))!.SetValue(app, new PomodoroService(store, () => app.Settings));
        var window = new MainWindow(app);
        foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (field.GetValue(window) is DispatcherTimer timer) timer.Stop();
        Invoke(window, "LoadCharacterAssets");
        var root = (Grid)window.Content;
        var layer = (Canvas)window.FindName("PetPropLayer");
        var panel = (Border)window.FindName("PetChoicePanel");
        var bar = (StackPanel)window.FindName("PetPropBar");
        var items = (StackPanel)window.FindName("PetChoiceItems");
        var kind = typeof(MainWindow).GetNestedType("PetChoiceKind", BindingFlags.NonPublic)!;
        foreach (var scale in new[] { .6, 1.0, 2.0 })
        {
            Invoke(window, "ApplyScale", scale, false);
            Invoke(window, "ApplyStateVisual", PetState.Idle);
            root.Measure(new System.Windows.Size(830, 590));
            root.Arrange(new Rect(0, 0, 830, 590)); root.UpdateLayout();
            layer.Visibility = Visibility.Visible;
            Invoke(window, "PositionPetProps");
            foreach (var (name, count) in new[] { ("Snacks", 5), ("Dances", 3) })
            {
                var choice = Enum.Parse(kind, name);
                Invoke(window, "ConfigurePetChoices", choice);
                panel.Visibility = Visibility.Visible;
                Invoke(window, "PositionPetChoices", choice);
                root.UpdateLayout();
                Require(items.Children.Count == count, $"Wrong number of {name} options.");
                var bounds = panel.TransformToAncestor(root).TransformBounds(new Rect(panel.RenderSize));
                var barBounds = bar.TransformToAncestor(root).TransformBounds(new Rect(bar.RenderSize));
                Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 830 && bounds.Bottom <= 590,
                    $"{name} chooser is clipped at {scale:P0}: {bounds}");
                Require(bounds.Right + 4 <= barBounds.Left,
                    $"{name} chooser overlaps the default props at {scale:P0}.");
                foreach (System.Windows.Controls.Button button in items.Children)
                {
                    var buttonBounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                    Require(bounds.Contains(buttonBounds), $"{name} option is clipped inside its chooser.");
                    var hit = VisualTreeHelper.HitTest(root, new System.Windows.Point(
                        buttonBounds.Left + buttonBounds.Width / 2, buttonBounds.Top + buttonBounds.Height / 2))?.VisualHit;
                    while (hit is not null && !ReferenceEquals(hit, button)) hit = VisualTreeHelper.GetParent(hit);
                    Require(ReferenceEquals(hit, button), $"{name} choice cannot be clicked at {scale:P0}.");
                }
                Save(root, 830, 590, Path.Combine(directory, $"{name.ToLowerInvariant()}-scale-{scale:0.0}.png"));
            }
            Invoke(window, "HidePetProps");
            var cloud = (WpfImage)window.FindName("AiCompanion");
            var character = (WpfImage)window.FindName("CharacterImage");
            var animator = (PetCompanionAnimator)typeof(MainWindow)
                .GetField("_companionAnimator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var dance = new PetAnimationLibrary(Path.Combine(AppContext.BaseDirectory,
                "assets", "character", "animations")).Load("DanceWingTail")!;
            character.Source = dance.Frames[dance.Clip.PosterFrame];
            animator.SetState(PetState.Happy, false, true);
            animator.ReactDance(PetDance.WingTail, 8000);
            root.UpdateLayout();
            var cloudBounds = cloud.TransformToAncestor(root).TransformBounds(new Rect(cloud.RenderSize));
            Save(root, 830, 590, Path.Combine(directory, $"companion-dance-scale-{scale:0.0}.png"));
            // The constrained offscreen host may compress its arranged height slightly at 200%.
            // The logical cloud size must stay fixed; the rendered cloud must remain in bounds.
            Require(cloud.Visibility == Visibility.Visible && cloud.Width == 18 && cloud.Height == 15
                && Math.Abs(cloudBounds.Width - 18 * scale) < 1
                && Math.Abs(cloudBounds.Height - 15 * scale) < 2
                && cloudBounds.Left >= 0 && cloudBounds.Top >= 0
                && cloudBounds.Right <= 830 && cloudBounds.Bottom <= 590,
                $"The dancing companion changed size or escaped the window at {scale:P0}: {cloudBounds}.");
            animator.Suspend();
        }
        Invoke(window, "HidePetProps");
        Require(panel.Visibility == Visibility.Collapsed && layer.Visibility == Visibility.Collapsed,
            "Hidden pet still has an open choice panel.");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Console.WriteLine("PASS: five/three choice targets visible and clickable at 60%, 100%, 200%; hide closes chooser.");
    }

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
        Require(shown == 1 && completed == 1 && !player.IsPlaying,
            "Finite clip did not stop and complete exactly once.");
        player.Play(animation, false, () => completed++);
        player.Stop(); advance.Invoke(player, [200L]);
        Require(completed == 1 && !player.IsPlaying, "Stopped clip completed after interruption.");
        player.Play(animation, true, () => completed++);
        Require(shown == 3 && completed == 1 && !player.IsPlaying,
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
        var propLayer = (Canvas)window.FindName("PetPropLayer");
        var propBar = (StackPanel)window.FindName("PetPropBar");
        var propSnack = (System.Windows.Controls.Button)window.FindName("PetPropSnack");
        var propDance = (System.Windows.Controls.Button)window.FindName("PetPropDance");
        Require(propLayer.Visibility == Visibility.Collapsed
            && propSnack.Width == 34 && propDance.Width == 34
            && propSnack.ToolTip?.ToString()?.Contains("投喂") == true
            && propDance.ToolTip?.ToString()?.Contains("舞") == true,
            $"The direct props are not initially hidden or labelled: {propLayer.Visibility}, "
                + $"snack={propSnack.ToolTip}, dance={propDance.ToolTip}.");
        propLayer.Visibility = Visibility.Visible;
        Invoke(window, "ToggleChat");
        Require(propLayer.Visibility == Visibility.Collapsed,
            "Direct props stayed visible behind the chat bubble.");
        Invoke(window, "ToggleChat");
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
            propLayer.Visibility = Visibility.Visible;
            Invoke(window, "PositionPetProps");
            root.UpdateLayout();
            var propBounds = propBar.TransformToAncestor(root).TransformBounds(new Rect(propBar.RenderSize));
            var cloudBounds = companionHitArea.TransformToAncestor(root)
                .TransformBounds(new Rect(companionHitArea.RenderSize));
            Require(propBounds.Left >= 0 && propBounds.Top >= 0
                && propBounds.Right <= 830 && propBounds.Bottom <= 590
                && !propBounds.IntersectsWith(cloudBounds),
                $"The snack/dance props are clipped or cover the small AI at {scaleValue:P0}.");
            foreach (var prop in new[] { propSnack, propDance })
            {
                var bounds = prop.TransformToAncestor(root).TransformBounds(new Rect(prop.RenderSize));
                var hit = VisualTreeHelper.HitTest(root,
                    new System.Windows.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2))?.VisualHit;
                while (hit is not null && !ReferenceEquals(hit, prop)) hit = VisualTreeHelper.GetParent(hit);
                Require(ReferenceEquals(hit, prop),
                    $"The {prop.Name} hit target is covered at {scaleValue:P0}.");
            }
            Save(root, 830, 590, Path.Combine(directory, $"direct-props-scale-{scaleValue:0.0}.png"));
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
            Require(propLayer.Visibility == Visibility.Collapsed,
                "Direct props stayed visible during a pet action.");
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
