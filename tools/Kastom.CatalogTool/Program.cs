using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kastom.Core;
using Kastom.Core.Catalog;
using Kastom.Core.Models;

Console.OutputEncoding = Encoding.UTF8;
return CatalogToolCli.Run(args);

internal sealed class UsageException(string message) : Exception(message);

internal static class CatalogToolCli
{
    private const string Help = """
        kastom-catalog — інструмент видавця модулів Kastom.

        Команди:
          keygen  --private <файл.pem> --public <файл.txt> [--force]
                  Створити пару ключів ECDSA P-256. Секретний ключ — PKCS#8 PEM, відкритий — SPKI base64.
                  Без --force наявний файл ключа не перезаписується.
          pack    --dir <папка збірки модуля> --out <файл.zip>
                  Запакувати збірку модуля (потрібен правильний module.json) у zip.
          catalog --packages <папка з zip> --base-url <адреса> --out <catalog.json>
                  [--launcher-package <Kastom-X.Y.Z-full.nupkg> --launcher-version <X.Y.Z>]
                  Скласти каталог із пакетів. Адреса: https://... або file:///C:/...
                  Обидва параметри --launcher-* разом додають у каталог реліз лаунчера
                  (SHA-256 і ім'я файлу повного пакета Velopack) для самооновлення.
          sign    --key <секретний.pem> --catalog <catalog.json>
                  Підписати точні байти каталогу; підпис пишеться в <catalog.json>.sig.
          verify  --public <файл або base64> --catalog <catalog.json> [--allow-file-urls]
                  Перевірити підпис і вміст каталогу, вивести список модулів.
          help    Показати цю довідку.

        Коди виходу: 0 — успіх, 1 — помилка виконання, 2 — неправильні аргументи.
        """;

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
            {
                Console.WriteLine(Help);
                return args.Length == 0 ? 2 : 0;
            }

            var command = args[0];
            return command switch
            {
                "keygen" => KeyGen(Options.Parse(args, ["private", "public"], ["force"])),
                "pack" => Pack(Options.Parse(args, ["dir", "out"], [])),
                "catalog" => BuildCatalog(Options.Parse(args, ["packages", "base-url", "out", "launcher-package", "launcher-version"], [])),
                "sign" => Sign(Options.Parse(args, ["key", "catalog"], [])),
                "verify" => Verify(Options.Parse(args, ["public", "catalog"], ["allow-file-urls"])),
                _ => throw new UsageException($"Невідома команда: '{command}'."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Помилка: {ex.Message}");
            Console.Error.WriteLine("Довідка: kastom-catalog help");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or InvalidDataException or JsonException or CatalogValidationException
                                       or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Помилка: {ex.Message}");
            return 1;
        }
    }

    private static int KeyGen(Options o)
    {
        var privatePath = Path.GetFullPath(o.Required("private"));
        var publicPath = Path.GetFullPath(o.Required("public"));
        var force = o.Flag("force");
        if (!force && (File.Exists(privatePath) || File.Exists(publicPath)))
            throw new IOException("Файл ключа вже існує. Щоб перезаписати (старий ключ буде втрачено), додайте --force.");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
        File.WriteAllText(publicPath, publicKey);

        Console.WriteLine($"Секретний ключ: {privatePath}");
        Console.WriteLine("Зберігайте його поза репозиторієм і поза Google Диском.");
        Console.WriteLine($"Відкритий ключ записано в {publicPath}. Вставте його в KastomTrust.CatalogPublicKey:");
        Console.WriteLine(publicKey);
        return 0;
    }

    private static int Pack(Options o)
    {
        var dir = Path.GetFullPath(o.Required("dir"));
        var outPath = Path.GetFullPath(o.Required("out"));
        if (!Directory.Exists(dir)) throw new IOException($"Папку збірки не знайдено: {dir}");

        var manifestPath = Path.Combine(dir, ModuleManifest.FileName);
        if (!File.Exists(manifestPath)) throw new IOException($"У папці немає {ModuleManifest.FileName}.");
        var manifest = ReadManifest(File.ReadAllBytes(manifestPath), manifestPath);
        if (!File.Exists(Path.Combine(dir, manifest.EntryAssembly)))
            throw new IOException($"Збірку '{manifest.EntryAssembly}' з module.json не знайдено в папці.");

        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFullPath(f), outPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Full: f, Name: Path.GetRelativePath(dir, f).Replace('\\', '/')))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        if (File.Exists(outPath)) File.Delete(outPath);
        using (var stream = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (full, name) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero); // щоб однакова збірка давала однаковий zip
                using var input = File.OpenRead(full);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }

        Console.WriteLine($"Запаковано {files.Count} файлів модуля '{manifest.Id}' {manifest.Version} → {outPath}");
        return 0;
    }

    private static int BuildCatalog(Options o)
    {
        var packages = Path.GetFullPath(o.Required("packages"));
        var baseUrl = o.Required("base-url").TrimEnd('/');
        var outPath = Path.GetFullPath(o.Required("out"));
        var launcher = ReadLauncher(o);
        if (!Directory.Exists(packages)) throw new IOException($"Папку з пакетами не знайдено: {packages}");

        var newest = new Dictionary<string, (CatalogEntry Entry, ModuleVersion Version)>(StringComparer.Ordinal);
        foreach (var zip in Directory.GetFiles(packages, "*.zip").OrderBy(f => f, StringComparer.Ordinal))
        {
            var manifest = ReadManifestFromZip(zip);
            var bytes = File.ReadAllBytes(zip);
            var entry = new CatalogEntry
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Description = manifest.Description,
                Version = manifest.Version,
                ContractVersion = manifest.ContractVersion,
                MinLauncherVersion = manifest.MinLauncherVersion,
                RunMode = manifest.RunMode,
                Url = baseUrl + "/" + Path.GetFileName(zip),
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Size = bytes.LongLength,
            };

            var problem = CatalogEntryValidator.Validate(entry, allowFileUrls: baseUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
            if (problem is not null) throw new CatalogValidationException($"{Path.GetFileName(zip)}: {problem}");

            var version = ModuleVersion.Parse(entry.Version);
            if (newest.TryGetValue(entry.Id, out var known))
            {
                Console.WriteLine($"Увага: модуль '{entry.Id}' має кілька пакетів; у каталог іде найновіший.");
                if (known.Version >= version) continue;
            }

            newest[entry.Id] = (entry, version);
        }

        var catalog = new ModuleCatalog
        {
            SchemaVersion = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            Modules = newest.Values.Select(v => v.Entry).OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            Launcher = launcher,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, JsonSerializer.SerializeToUtf8Bytes(catalog, KastomJson.Options));
        Console.WriteLine($"Каталог: {catalog.Modules.Count} модулів{(launcher is null ? "" : $", лаунчер {launcher.Version}")} → {outPath}");
        Console.WriteLine("Не забудьте підписати його: kastom-catalog sign.");
        return 0;
    }

    private static LauncherRelease? ReadLauncher(Options o)
    {
        var package = o.Optional("launcher-package");
        var version = o.Optional("launcher-version");
        if (package is null && version is null) return null;
        if (package is null || version is null)
            throw new UsageException("Параметри --launcher-package і --launcher-version вказуються разом.");
        if (!File.Exists(package)) throw new IOException($"Пакет лаунчера не знайдено: {package}");

        var launcher = new LauncherRelease
        {
            Version = version,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(package))),
            FileName = Path.GetFileName(package),
        };
        var problem = CatalogEntryValidator.ValidateLauncher(launcher);
        if (problem is not null) throw new CatalogValidationException(problem);
        return launcher;
    }

    private static int Sign(Options o)
    {
        var keyPath = o.Required("key");
        var catalogPath = Path.GetFullPath(o.Required("catalog"));
        if (!File.Exists(keyPath)) throw new IOException($"Секретний ключ не знайдено: {keyPath}");
        if (!File.Exists(catalogPath)) throw new IOException($"Каталог не знайдено: {catalogPath}");

        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));
        if (key.KeySize != 256) throw new CryptographicException("Ключ підпису має бути ECDSA P-256.");

        var signature = Convert.ToBase64String(key.SignData(File.ReadAllBytes(catalogPath), HashAlgorithmName.SHA256));
        var signaturePath = catalogPath + ".sig";
        File.WriteAllText(signaturePath, signature);
        Console.WriteLine($"Підпис записано: {signaturePath}");
        return 0;
    }

    private static int Verify(Options o)
    {
        var publicArg = o.Required("public");
        var catalogPath = Path.GetFullPath(o.Required("catalog"));
        var publicKey = File.Exists(publicArg) ? File.ReadAllText(publicArg).Trim() : publicArg.Trim();
        if (!File.Exists(catalogPath)) throw new IOException($"Каталог не знайдено: {catalogPath}");
        if (!File.Exists(catalogPath + ".sig")) throw new IOException($"Немає файлу підпису: {catalogPath}.sig");

        var catalog = new CatalogVerifier(publicKey).Verify(
            File.ReadAllBytes(catalogPath),
            File.ReadAllText(catalogPath + ".sig").Trim(),
            o.Flag("allow-file-urls"));

        Console.WriteLine($"Підпис правильний. Схема {catalog.SchemaVersion}, створено {catalog.GeneratedAt:u}, модулів: {catalog.Modules.Count}.");
        foreach (var m in catalog.Modules)
            Console.WriteLine($"  {m.Id} {m.Version} — {m.Name} ({m.Size} байтів, sha256 {m.Sha256[..12]}…)");
        return 0;
    }

    private static ModuleManifest ReadManifestFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(ModuleManifest.FileName)
            ?? throw new InvalidDataException($"{Path.GetFileName(zipPath)}: у корені архіву немає {ModuleManifest.FileName}.");
        if (entry.Length > 1024 * 1024) throw new InvalidDataException($"{Path.GetFileName(zipPath)}: module.json завеликий.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return ReadManifest(buffer.ToArray(), zipPath);
    }

    /// <summary>Читає module.json і перевіряє поля, які потрібні каталогу й встановленню.</summary>
    private static ModuleManifest ReadManifest(byte[] json, string source)
    {
        ModuleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(json, KastomJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: module.json не прочитано: {ex.Message}");
        }

        if (manifest is null) throw new InvalidDataException($"{source}: module.json порожній.");
        if (!ModuleManifest.IsValidId(manifest.Id)) throw new InvalidDataException($"{source}: неправильний id '{manifest.Id}'.");
        if (!CatalogEntryValidator.IsSafeVersion(manifest.Version) || !ModuleVersion.TryParse(manifest.Version, out _))
            throw new InvalidDataException($"{source}: неправильна версія '{manifest.Version}'.");
        if (manifest.ContractVersion <= 0) throw new InvalidDataException($"{source}: contractVersion має бути додатним.");
        if (string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException($"{source}: порожній entryType.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || manifest.EntryAssembly != Path.GetFileName(manifest.EntryAssembly))
            throw new InvalidDataException($"{source}: entryAssembly має бути іменем файлу без шляху.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException($"{source}: порожня назва (name).");
        return manifest;
    }

    /// <summary>Ручний розбір "--ім'я значення" і прапорців "--ім'я".</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        public static Options Parse(string[] args, string[] valueNames, string[] flagNames)
        {
            var result = new Options();
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    throw new UsageException($"Несподіваний аргумент: '{arg}'.");
                var name = arg[2..];
                if (flagNames.Contains(name))
                {
                    result._flags.Add(name);
                }
                else if (valueNames.Contains(name))
                {
                    if (i + 1 >= args.Length) throw new UsageException($"Після {arg} потрібне значення.");
                    if (!result._values.TryAdd(name, args[++i])) throw new UsageException($"Параметр {arg} указано двічі.");
                }
                else
                {
                    throw new UsageException($"Невідомий параметр: {arg}.");
                }
            }

            return result;
        }

        public string Required(string name) =>
            _values.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new UsageException($"Не вказано обов'язковий параметр --{name}.");

        public string? Optional(string name) => _values.GetValueOrDefault(name);

        public bool Flag(string name) => _flags.Contains(name);
    }
}
