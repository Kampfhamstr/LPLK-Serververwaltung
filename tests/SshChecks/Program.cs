using System.Security.Cryptography;
using System.Text.Json;
using CS2ServerPortal;
using Renci.SshNet.Common;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

const string testPassword = "  local-ssh-fixture-password-ONLY  ";
string root = Path.Combine(Path.GetTempPath(), "CS2-SshChecks-" + Guid.NewGuid().ToString("N"));
string path = Path.Combine(root, "private", "ssh-connection.json");
try
{
    var connection = new SshConnection("debian.example.test", 2222, "root", testPassword, "SHA256:" + new string('A', 43));
    var store = new SshCredentialStore(root);
    Check(store.Read() == null, "Empty store must not appear configured.");
    store.Save(connection);
    var storedText = File.ReadAllText(path);
    Check(!storedText.Contains(testPassword) && !storedText.Contains(connection.Host), "Credentials or endpoint were written in plaintext.");
    Check(store.Read() == connection, "Encrypted credentials did not round-trip exactly, including password whitespace.");
    using (var document = JsonDocument.Parse(storedText))
    {
        var cipher = Convert.FromBase64String(document.RootElement.GetProperty("protectedConnection").GetString()!);
        cipher[^1] ^= 1;
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, protectedConnection = Convert.ToBase64String(cipher) }));
        bool rejected = false;
        try { store.Read(); } catch (CryptographicException) { rejected = true; }
        Check(rejected, "A modified credential store was accepted.");
    }
    foreach (var host in new[] { "ssh://example.test", "root@example.test", "example.test:22", "host;uname", "host name", "" })
    {
        bool rejected = false;
        try { SshCredentialStore.ValidateHost(host); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Invalid endpoint accepted.");
    }
    Check(SshCredentialStore.ValidateHost("[::1]") == "::1", "IPv6 hostname handling failed.");
    Check(!SshService.HostKeyMatches("", "") && !SshService.HostKeyMatches("SHA256:abc", "SHA256:Abc"), "Host key comparison accepted an unknown or changed key.");
    Check(!SshService.CleanOutput("hello " + testPassword + "\u001b\n", testPassword).Contains(testPassword), "Diagnostic redaction failed.");
    Console.WriteLine("PASS: encrypted credential storage, tamper rejection, endpoint validation, secret redaction.");
}
finally
{
    if (File.Exists(path)) File.Delete(path);
    if (Directory.Exists(Path.Combine(root, "private"))) Directory.Delete(Path.Combine(root, "private"));
    if (Directory.Exists(root)) Directory.Delete(root);
}

if (args.Length == 2)
{
    int port = int.Parse(args[0]);
    string fingerprint = args[1];
    var connection = new SshConnection("127.0.0.1", port, "root", testPassword, fingerprint);
    var observed = SshService.InspectHostKey(connection.Host, connection.Port);
    Check(observed.Sha256 == fingerprint, "Server fingerprint discovery failed.");
    var result = SshService.TestConnection(connection);
    Check(result.ExitCode == 0 && result.Output.Contains("root"), "Password login or command execution failed.");
    bool keyRejected = false;
    try { SshService.TestConnection(connection with { HostKeySha256 = "SHA256:" + new string('A', 43) }); }
    catch (SshHostKeyException) { keyRejected = true; }
    Check(keyRejected, "Changed server key was accepted.");
    bool passwordRejected = false;
    try { SshService.TestConnection(connection with { Password = "wrong-fixture-password" }); }
    catch (SshAuthenticationException) { passwordRejected = true; }
    Check(passwordRejected, "Wrong password was accepted.");
    var diagnostic = SshService.Diagnose(connection);
    Check(diagnostic.Results.Count == 5 && diagnostic.Results.All(r => r.ExitCode == 0), "Diagnostic collection failed.");
    Check(diagnostic.Results.All(r => !r.Output.Contains(testPassword)), "Remote response exposed the stored password.");
    Console.WriteLine("PASS: SSH handshake, host key probe, password login, changed-key rejection, wrong-password rejection, read-only diagnostics.");
}
