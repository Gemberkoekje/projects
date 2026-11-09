namespace DatumPrikker.test.Models;

public sealed class Answer_specs
{
    [Test]
    public void an_answer_is_keyed_by_poll_and_normalised_email()
    {
        var poll = Fixtures.Poll();

        var answer = Answer.Give(poll, Fixtures.AnswerFrom(" Carla ", "  Carla@Example.COM ", poll, Availability.Yes), Account.Anonymous, Fixtures.Now);

        answer.IsValid.Should().BeTrue();
        answer.Value.Email.Should().Be("carla@example.com");
        answer.Value.Name.Should().Be("Carla");
        answer.Value.Id.Should().Be(Answer.IdFor(poll.Id, "carla@example.com"));
        answer.Value.AccountId.Should().BeEmpty();
    }

    [Test]
    public void dates_left_out_count_as_no_answer()
    {
        var poll = Fixtures.Poll();

        var answer = Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Yes);

        answer.ChoiceFor(poll.Options[0].Id).Should().Be(Availability.Yes);
        answer.ChoiceFor(poll.Options[1].Id).Should().Be(Availability.None);
        answer.Choices.Should().ContainSingle();
    }

    [Test]
    public void a_closed_poll_takes_no_answers()
    {
        var poll = Fixtures.Poll().Close(Fixtures.Owner, Fixtures.Now).Value;

        Answer.Give(poll, Fixtures.AnswerFrom("Carla", "carla@example.com", poll, Availability.Yes), Account.Anonymous, Fixtures.Now)
            .Errors().Should().Contain("Deze prikker is gesloten en neemt geen antwoorden meer aan.");
    }

    [TestCase("")]
    [TestCase("geen-adres")]
    [TestCase("carla@")]
    public void an_anonymous_answer_needs_a_valid_email(string email)
    {
        var poll = Fixtures.Poll();

        Answer.Give(poll, Fixtures.AnswerFrom("Carla", email, poll, Availability.Yes), Account.Anonymous, Fixtures.Now)
            .Errors().Should().Contain("Vul een geldig e-mailadres in.");
    }

    [Test]
    public void an_answer_needs_a_name_and_at_least_one_choice()
    {
        var poll = Fixtures.Poll();

        var errors = Answer.Give(poll, Fixtures.AnswerFrom(" ", "carla@example.com", poll, Availability.None), Account.Anonymous, Fixtures.Now).Errors();

        errors.Should().Contain("Vul je naam in.")
            .And.Contain("Kies bij minstens één datum Ja, Misschien of Nee.");
    }

    [Test]
    public void an_answer_cannot_name_a_date_of_another_poll()
    {
        var poll = Fixtures.Poll();
        var draft = Fixtures.AnswerFrom("Carla", "carla@example.com", poll, Availability.Yes);
        var foreign = draft with { Choices = [.. draft.Choices, new OptionAnswer { OptionId = Guid.NewGuid(), Availability = Availability.Yes }] };

        Answer.Give(poll, foreign, Account.Anonymous, Fixtures.Now)
            .Errors().Should().Contain("Je antwoord noemt een datum die niet bij deze prikker hoort.");
    }

    [Test]
    public void signed_in_you_answer_as_your_account_email_whatever_you_typed()
    {
        var poll = Fixtures.Poll();

        var answer = Answer.Give(poll, Fixtures.AnswerFrom("Bea", "someone-else@example.com", poll, Availability.Yes), Fixtures.Stranger, Fixtures.Now);

        answer.Value.Email.Should().Be("bea@example.com");
        answer.Value.AccountId.Should().Be("microsoft:222");
    }
}
