using Curator.Core.Commands;
using Curator.Core.Game;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Shots;

/// <summary>
/// Screenshot mode (BUILD_BRIEF §7.11): plays a scripted sequence through the controller — the same
/// calls the mouse makes — saves PNGs and quits. Needs a renderer, so it runs windowed (under Xvfb
/// in the cloud).
/// </summary>
public partial class ShotRunner : Node
{
    private string directory = "";
    // Unset until Setup (a justified nullable: nothing to photograph yet).
    private GameController? controller;
    private int shot;

    private GameController Game => controller ?? throw new InvalidOperationException("Set up the shot runner first.");

    /// <summary>Prepares the run.</summary>
    /// <param name="outputDirectory">Where the PNGs go.</param>
    /// <param name="gameController">The game to play.</param>
    public void Setup(string outputDirectory, GameController gameController)
    {
        directory = outputDirectory;
        controller = gameController;
    }

    /// <inheritdoc />
    public override async void _Ready()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(directory);
            GetWindow().Size = new Vector2I(1920, 1080);
            await Seconds(0.3);
            await PlayDayOne();
            await PlayDayTwo();
            GD.Print($"Shots written to {directory}: {string.Join(", ", Game.World.ArtSources().Select(a => $"{a.Slot}={a.Source}"))}");
            GetTree().Quit(0);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            GD.PushError($"Screenshot run failed: {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task PlayDayOne()
    {
        var camera = Game.World.Camera;
        camera.LookUp(immediate: true);
        await Shoot("day1_morning_up");
        camera.LookDown(immediate: true);
        await Shoot("day1_morning_down");
        Game.Open(DeskDocument.Letters);
        await Shoot("day1_letters");
        Game.Open(DeskDocument.None);

        Game.RingBell();
        await Seconds(1.0);
        await Shoot("day1_clerk_arrives");
        var visit = Game.Session.Views.Visit();
        var question = visit.Questions.FirstOrDefault(q => !q.Asked);
        if (question is not null)
        {
            Game.Send(new AskQuestion(question.Id));
            await Shoot("day1_clerk_question");
        }

        camera.LookDown();
        await Seconds(0.6);
        Game.Open(DeskDocument.Catalogue);
        await Shoot("day1_catalogue");
        Game.OpenBook(visit.RequestedBookId);
        Game.Send(new Identify(visit.RequestedBookId));
        await Shoot("day1_book_after_identify");
        Game.Send(new LendRequested());
        await Shoot("day1_lent");
        var loan = Game.Session.Views.Visit().NoteLoanId;
        if (loan.Length > 0)
        {
            Game.Send(new NoteLoan(loan));
        }

        Game.Open(DeskDocument.Notebook);
        await Shoot("day1_notebook");
        Game.Open(DeskDocument.None);
        Game.RequestClose();
        await Seconds(1.8);
        await Shoot("day1_summary");
    }

    private async Task PlayDayTwo()
    {
        var camera = Game.World.Camera;
        PressSummaryButton();
        await Seconds(1.8);
        camera.LookDown(immediate: true);
        Game.Open(DeskDocument.Newspaper);
        await Shoot("day2_newspaper");
        Game.Open(DeskDocument.None);
        for (var i = 0; i < 3 && Game.Session.Views.Hud().CanRingBell; i++)
        {
            Game.RingBell();
            await Seconds(1.0);
            if (i == 2)
            {
                Game.Send(new ReadThoughts());
                await Seconds(1.8);
                await Shoot("day2_third_visit_up");
            }

            Settle();
            if (i == 2)
            {
                await Seconds(1.8);
                await Shoot("day2_dusk_farewell");
                camera.LookDown(immediate: true);
                await Shoot("day2_dusk_desk");
            }
        }

        Game.RequestClose();
        await Seconds(1.8);
        await Shoot("day2_summary");
    }

    // Settles the visit at the counter plainly: lend if allowed, else decline, else offer a shelved book.
    private void Settle()
    {
        var visit = Game.Session.Views.Visit();
        if (visit.CanLendRequested)
        {
            Game.Send(new LendRequested());
        }
        else if (visit.CanDecline)
        {
            Game.Send(new Decline());
        }
        else
        {
            var book = Game.Session.Views.Catalogue().Cards.First(c => c.Status == BookStatus.OnShelf);
            Game.Send(new OfferBook(book.BookId));
        }
    }

    private void PressSummaryButton()
    {
        var summary = GetTree().Root.FindChild("DaySummary", true, false) as Control;
        var button = summary?.FindChildren("*", "Button", true, false).OfType<Button>().LastOrDefault();
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Shoot(string name)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != 1920)
        {
            image.Resize(1920, 1080, Image.Interpolation.Lanczos);
        }

        shot++;
        var path = Path.Combine(directory, $"{shot:00}_{name}.png");
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            throw new IOException($"Couldn't save {path}: {error}");
        }
    }
}
