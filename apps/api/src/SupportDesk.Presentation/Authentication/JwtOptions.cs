using System.Text;
namespace SupportDesk.Presentation.Authentication;

public sealed class JwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Northwind.SupportDesk";
    public string Audience { get; set; } = "Northwind.SupportDesk.Web";
    public int TokenLifetimeMinutes { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Configure Jwt__SigningKey with at least 32 bytes of random secret material. See README.");
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || TokenLifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("JWT issuer/audience and a lifetime of 1-1440 minutes are required.");
    }
}
