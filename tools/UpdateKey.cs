// Signs Mini Local Chat releases for the LAN auto-update. Run from the repo folder:
//
//   dotnet run tools/UpdateKey.cs -- new           once: creates the private signing key and prints the public key
//   dotnet run tools/UpdateKey.cs -- sign <exe>    writes <exe>.sig next to the published exe
//
// The private key lives in %USERPROFILE%\.minilocalchat\update-signing-key.pem (or the path in the
// MLC_UPDATE_KEY environment variable). Keep it, and back it up: only exes signed with it are accepted
// as updates. The matching public key is in src\MiniLocalChat\Services\UpdateSigning.cs.
#:property PublishAot=false
#:property PublishTrimmed=false

using System.IO;
using System.Security.Cryptography;

var keyPath = Environment.GetEnvironmentVariable("MLC_UPDATE_KEY")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minilocalchat", "update-signing-key.pem");

switch (args)
{
    case ["new"]:
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine($"A key already exists: {keyPath}\nDelete it first if you really want a new one (installed apps will then refuse updates signed with it).");
            return 1;
        }

        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Private key: {keyPath}  (keep it safe, back it up)");
            Console.WriteLine("Public key (goes into UpdateSigning.cs):");
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case ["public"]:
        using (var key = LoadKey())
        {
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case ["sign", var exe]:
        using (var key = LoadKey())
        using (var file = File.OpenRead(exe))
        {
            var signature = key.SignData(file, HashAlgorithmName.SHA256);
            File.WriteAllText(exe + ".sig", Convert.ToBase64String(signature));
            Console.WriteLine($"Signed: {exe}.sig");
        }

        return 0;

    default:
        Console.Error.WriteLine("Usage: dotnet run tools/UpdateKey.cs -- new | public | sign <exe>");
        return 2;
}

ECDsa LoadKey()
{
    if (!File.Exists(keyPath))
    {
        throw new FileNotFoundException($"No signing key at {keyPath}. Create one with: dotnet run tools/UpdateKey.cs -- new");
    }

    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(keyPath));
    return key;
}
