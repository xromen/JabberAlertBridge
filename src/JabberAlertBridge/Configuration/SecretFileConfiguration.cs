namespace JabberAlertBridge.Configuration;

public static class SecretFileConfiguration
{
    public static void AddSecretFiles(this ConfigurationManager configuration)
    {
        string[] keys = ["ConnectionStrings:Postgres", "WebhookAuth:Password", "Xmpp:Password"];

        foreach (string key in keys)
        {
            string? file = configuration[$"{key}File"];

            if (!string.IsNullOrWhiteSpace(file))
            {
                configuration[key] = File.ReadAllText(file).TrimEnd('\r', '\n');
            }
        }
    }
}
