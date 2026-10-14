using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal static class PanelAccessErrors
{
    public static void ValidateClientKey(string key)
    {
        if (key.StartsWith("ptla_", StringComparison.Ordinal))
            throw new ArgumentException("Das ist ein Application-API-Schlüssel. Bitte im persönlichen Panel-Konto unter /account/api einen Client-API-Schlüssel erstellen und hier eintragen.");
        if (key.Length < 20 || key.Any(char.IsWhiteSpace))
            throw new ArgumentException("Bitte den vollständigen Client-API-Schlüssel aus deinem Panel-Konto unter /account/api eingeben.");
    }

    public static string Describe(HttpStatusCode status, string body, string? mediaType)
    {
        if (status == HttpStatusCode.Forbidden)
        {
            // Inspect known error shapes, but never expose raw server messages or HTML.
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var error in errors.EnumerateArray())
                    {
                        if (error.ValueKind != JsonValueKind.Object) continue;
                        string code = Text(error, "code"), detail = Text(error, "detail");
                        if (code == "TwoFactorAuthRequiredException")
                            return "Das Panel verlangt für dieses Konto Zwei-Faktor-Authentifizierung (403). Bitte 2FA im persönlichen Panel-Konto aktivieren und danach erneut prüfen.";
                        if (detail.Contains("does not have permission to access the API using these credentials", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(detail, @"This IP address \(([^)\r\n]{1,64})\)", RegexOptions.IgnoreCase);
                            string address = match.Success && IPAddress.TryParse(match.Groups[1].Value, out var ip) ? " Die vom Panel erkannte Absender-IP ist " + ip + "." : "";
                            return "Die IP-Beschränkung dieses API-Schlüssels blockiert den Zugriff (403)." + address + " In „Allowed IPs“ muss die öffentliche Absender-IP dieses PCs zugelassen sein; die IP des Debian-Servers genügt dafür nicht.";
                        }
                    }
                }
            }
            catch (JsonException) { }
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                return "Der Webserver oder ein vorgeschalteter Zugriffsschutz verweigert die Anfrage mit einer HTML-Seite (403). Bitte die Zugriffsregeln für /api/client prüfen; der Schlüssel ist damit noch nicht als falsch nachgewiesen.";
            return "Das Panel verweigert den Zugriff (403). Bitte Client-Schlüssel unter /account/api, dessen erlaubte Absender-IPs, die Kontoberechtigungen und eine mögliche 2FA-Pflicht prüfen.";
        }
        return status switch
        {
            HttpStatusCode.Unauthorized => "Das Panel hat den API-Schlüssel abgelehnt (401). Bitte den vollständigen, gültigen Client-API-Schlüssel aus /account/api verwenden.",
            HttpStatusCode.NotFound => "Die angefragte Panel-Datei oder API-Adresse wurde nicht gefunden (404).",
            HttpStatusCode.TooManyRequests => "Das Panel begrenzt die Anfragen vorübergehend (429). Bitte später erneut versuchen.",
            _ when (int)status is >= 300 and < 400 => "Das Panel leitet auf eine andere Adresse weiter. Bitte die endgültige HTTPS-Paneladresse eintragen.",
            _ => $"Das Panel meldet HTTP {(int)status}."
        };
    }

    private static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
}
