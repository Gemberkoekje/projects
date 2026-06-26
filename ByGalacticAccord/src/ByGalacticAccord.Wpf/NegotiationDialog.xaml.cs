using System.Globalization;
using System.Windows;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Wpf;

/// <summary>
/// The Negotiation View (§4.1.6) as a modal dialog: the player counters, accepts, or walks against a
/// readable soft tell, never seeing the counterparty's hidden reservation. On a deal it commits the
/// haul through the shared <see cref="PlayerActions.AcceptHaul"/> path.
/// </summary>
public partial class NegotiationDialog : Window
{
    private readonly SimulationContext _ctx;
    private readonly ActorState _player;
    private readonly Contract _contract;
    private readonly PlayerActions.HaulFeasibility _feasibility;
    private readonly NegotiationSession? _session;

    public NegotiationDialog(SimulationContext ctx, ActorState player, Contract contract, PlayerActions.HaulFeasibility feasibility)
    {
        InitializeComponent();
        _ctx = ctx;
        _player = player;
        _contract = contract;
        _feasibility = feasibility;

        ActorState consumer = ctx.Actor(contract.PostedBy);
        TitleText.Text = $"{consumer.Name} wants {contract.Quantity:0} {contract.Good}";
        RouteText.Text = $"{ctx.Location(contract.OriginLocationId).Name} → {ctx.Location(contract.DestinationLocationId).Name}"
                         + (feasibility.RepoTicks > 0 ? $"   (reposition ~{feasibility.RepoTicks} ticks first)" : string.Empty);

        if (!feasibility.Ok)
        {
            TermsText.Text = feasibility.Reason;
            DealPanel.Visibility = Visibility.Collapsed;
            OfferText.Text = "—";
            TellText.Text = string.Empty;
            CloseBtn.Visibility = Visibility.Visible;
            return;
        }

        TermsText.Text = $"Goods cost (to producer): {contract.GoodsCost}    ·    Your bond if you take it: {contract.PerformanceBond}";
        _session = new NegotiationSession(
            new NegotiationParty(contract.BuyerReservationFee, consumer.Personality.ConcessionRate, IsBuyer: true),
            contract.OfferedPayment,
            ctx.Config.NegotiationMaxRounds);

        AskBox.Text = contract.OfferedPayment.Amount.ToString("0", CultureInfo.InvariantCulture);
        ShowOffer();
    }

    private void ShowOffer()
    {
        if (_session is null)
        {
            return;
        }

        OfferText.Text = _session.CurrentOffer.ToString();
        TellText.Text = $"they {_session.Tell}";
    }

    private void OnCounter(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (!decimal.TryParse(AskBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal ask))
        {
            TellText.Text = "enter a number to counter with";
            return;
        }

        NegotiationStep step = _session.Counter(Credits.Of(ask));
        switch (step.Kind)
        {
            case NegotiationStepKind.Accepted:
                Commit(step.Fee);
                break;
            case NegotiationStepKind.Walked:
                Conclude("They walked away. No deal.", success: false);
                break;
            default:
                ShowOffer();
                break;
        }
    }

    private void OnAcceptOffer(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            Commit(_session.CurrentOffer);
        }
    }

    private void OnWalk(object sender, RoutedEventArgs e) => Conclude("You walked away from the table.", success: false);

    private void Commit(Credits fee)
    {
        (bool ok, string message) = PlayerActions.AcceptHaul(_ctx, _player, _contract, _feasibility.Ship!, fee, _feasibility.RepoTicks);
        Conclude(message, ok);
    }

    private void Conclude(string message, bool success)
    {
        DealPanel.Visibility = Visibility.Collapsed;
        ResultText.Text = message;
        ResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
        if (!success)
        {
            ResultText.Foreground = System.Windows.Media.Brushes.Salmon;
        }

        CloseBtn.Visibility = Visibility.Visible;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
