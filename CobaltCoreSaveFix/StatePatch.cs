using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Serialization;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ITRsSaveFix;

[HarmonyPatch(typeof(State), nameof(State.Load))]
public static class LoadPatch
{
    private static readonly JsonSerializer Deserializer = JsonSerializer.Create(
        new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = FeatureFlags.Debug ? Formatting.Indented : Formatting.None
        }
    );

    // ReSharper disable once InconsistentNaming
    static void Postfix(ref State.SaveSlot __result, int slot)
    {
        var logger = ModManifest.Instance!.Logger!;
        if (__result.isCorrupted)
        {
            var path = State.GetSavePath(slot);
            if (!Storage.Exists(path))
            {
                return;
            }

            var fullPath = Storage.SavePath(path);

            // Backup savefile because the game will overwrite it
            var newFileName = GenerateTimestampedFileName(fullPath);
            var newPath = Path.Combine(Path.GetDirectoryName(fullPath)!, newFileName);
            File.Copy(fullPath, newPath);

            // Use custom loader instead
            try
            {
                __result.state = Load(fullPath);
                __result.isCorrupted = false;
            }
            catch (Exception e)
            {
                __result.isCorrupted = true;
                logger.Log(LogLevel.Error, e, "Encountered exception during extra deserialization");
                return;
            }
        }
        
        RemoveStatuses(__result, logger);
        RemoveCharacters(__result, logger);
    }

    private static void RemoveStatuses(State.SaveSlot result, ILogger logger)
    {
        var keys = new List<Status>(
            result.state?.ship.statusEffects.Keys?.ToArray() ??
            ArraySegment<Status>.Empty
        );
        keys.RemoveAll(DB.statuses.ContainsKey);
        foreach (var status in keys)
        {
            result.state?.ship.statusEffects.Remove(status);
            logger.Log(LogLevel.Information, $"Removing status from save: {status}");
        }
    }
    
    private static void RemoveCharacters(State.SaveSlot result, ILogger logger)
    {
        var charsToRemove = result.state?.characters.Where(
            character => (character.deckType.HasValue && !DB.decks.ContainsKey(character.deckType.GetValueOrDefault())) ||
                         !DB.charPanels.ContainsKey(character.type)
        ).ToArray() ?? ArraySegment<Character>.Empty;
        foreach (var charToRemove in charsToRemove)
        {
            logger.Log(LogLevel.Information, $"Removing character from save: {charToRemove.type}");
            result.state?.characters.Remove(charToRemove);
        }
    }

    private static string GenerateTimestampedFileName(string originalPath)
    {
        // Extract original filename and extension
        var fileName = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);

        // Generate timestamp
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Combine filename, timestamp, and extension
        var newFileName = $"{fileName}.{timestamp}{extension}";

        return newFileName;
    }


    private static State? Load(string path)
    {
        using var fileStream = File.OpenRead(Storage.SavePath(path));
        using var streamReader = new StreamReader(
            path.EndsWith(".gz") ? new GZipStream(fileStream, CompressionMode.Decompress) : fileStream
        );
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            SerializationBinder = new JsonSerializationBinder(new DefaultSerializationBinder()),
            Error = (sender, args) =>
            {
                if (args.CurrentObject == args.ErrorContext.OriginalObject &&
                    args.ErrorContext.Error.InnerExceptionsAndSelf().OfType<JsonSerializationBinderException>().Any() &&
                    args.ErrorContext.OriginalObject!.GetType()
                        .GetInterfaces()
                        .Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>)))
                {
                    args.ErrorContext.Handled = true;
                }
            }
        };
        return JsonConvert.DeserializeObject<State>(streamReader.ReadToEnd(), settings);
    }

    private class JsonSerializationBinder : ISerializationBinder
    {
        readonly ISerializationBinder _binder;

        public JsonSerializationBinder(ISerializationBinder binder)
        {
            this._binder = binder ?? throw new ArgumentNullException();
        }

        public Type BindToType(string? assemblyName, string typeName)
        {
            try
            {
                return _binder.BindToType(assemblyName, typeName);
            }
            catch (Exception ex)
            {
                throw new JsonSerializationBinderException(ex.Message, ex);
            }
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            _binder.BindToName(serializedType, out assemblyName, out typeName);
        }
    }

    private class JsonSerializationBinderException : JsonSerializationException
    {
        public JsonSerializationBinderException()
        {
        }

        public JsonSerializationBinderException(string message) : base(message)
        {
        }

        public JsonSerializationBinderException(string message, Exception innerException) : base(
            message,
            innerException
        )
        {
        }

        public JsonSerializationBinderException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}