using DatumPrikker.WebApp.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace DatumPrikker.test.Web;

public sealed class RespondentMemory_specs
{
    private readonly RespondentMemory memory = new RespondentMemory(new EphemeralDataProtectionProvider());

    [Test]
    public void a_written_respondent_is_read_back()
    {
        var cookie = Written("Carla de Vries", "carla@example.com");

        var remembered = memory.Read(RequestWith(cookie));

        remembered.Should().BeEquivalentTo(new Remembered { Name = "Carla de Vries", Email = "carla@example.com" });
    }

    [Test]
    public void the_cookie_is_protected_not_readable()
    {
        Written("Carla", "carla@example.com").Should().NotContain("carla");
    }

    [TestCase("")]
    [TestCase("carla@example.com")]
    [TestCase("bm90LXByb3RlY3RlZA")]
    public void a_missing_or_forged_cookie_remembers_nobody(string value)
    {
        memory.Read(RequestWith(value)).IsNobody.Should().BeTrue();
    }

    [Test]
    public void a_cookie_from_another_key_ring_remembers_nobody()
    {
        var other = new RespondentMemory(new EphemeralDataProtectionProvider());
        var context = new DefaultHttpContext();
        other.Write(context.Response, "Carla", "carla@example.com");

        memory.Read(RequestWith(CookieValue(context.Response))).IsNobody.Should().BeTrue();
    }

    private string Written(string name, string email)
    {
        var context = new DefaultHttpContext();
        memory.Write(context.Response, name, email);
        return CookieValue(context.Response);
    }

    private static string CookieValue(HttpResponse response)
    {
        var header = response.Headers.SetCookie.ToString();
        var start = header.IndexOf('=', StringComparison.Ordinal) + 1;
        var end = header.IndexOf(';', StringComparison.Ordinal);
        return Uri.UnescapeDataString(header[start..end]);
    }

    private static HttpRequest RequestWith(string cookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{RespondentMemory.CookieName}={Uri.EscapeDataString(cookie)}";
        return context.Request;
    }
}
