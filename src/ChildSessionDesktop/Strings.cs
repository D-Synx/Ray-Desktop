using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ChildSessionDesktop
{
    internal static class Strings
    {
        private static readonly Regex LanguageTag = new Regex(
            @"\A[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*\z", RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, string> English = ReadEmbedded("en");
        internal static readonly KeyValuePair<string, string>[] Languages =
        {
            new KeyValuePair<string, string>("de", "Deutsch"),
            new KeyValuePair<string, string>("en", "English"),
            new KeyValuePair<string, string>("es", "Español"),
            new KeyValuePair<string, string>("fr", "Français"),
            new KeyValuePair<string, string>("it", "Italiano"),
            new KeyValuePair<string, string>("pl", "Polski"),
            new KeyValuePair<string, string>("pt-br", "Português (Brasil)")
        };
        private static Dictionary<string, string> active = English;
        private static Dictionary<string, string> bundled = English;

        internal static string Language { get; private set; }

        internal static string NormalizeLanguage(string tag)
        {
            tag = (tag ?? "").Trim();
            return tag.Length <= 64 && LanguageTag.IsMatch(tag) ? tag.ToLowerInvariant() : null;
        }

        internal static string ExtractLanguage(ref string[] args)
        {
            string language = null;
            var remaining = new List<string>(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                    language = arg.Substring("--lang=".Length);
                else if (arg.Equals("--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                    && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    language = args[++i];
                else
                    remaining.Add(arg);
            }
            args = remaining.ToArray();
            return language;
        }

        public static void SetLanguage(string language, string directory = null)
        {
            string tag = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language;
            Language = NormalizeLanguage(tag) ?? "en";

            directory = directory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "languages");
            bundled = new Dictionary<string, string>(English, StringComparer.Ordinal);
            active = new Dictionary<string, string>(English, StringComparer.Ordinal);
            Overlay(active, ReadFile(directory, "en"));

            string culture = "";
            foreach (string part in Language.Split('-'))
            {
                culture = culture.Length == 0 ? part : culture + "-" + part;
                if (culture == "en") continue;
                // Brazilian Portuguese also supplies the base text for other Portuguese variants.
                var resource = culture == "pt-br" ? null : ReadEmbedded(culture == "pt" ? "pt-br" : culture);
                Overlay(bundled, resource);
                Overlay(active, resource);
                if (culture == "pt")
                    Overlay(active, ReadFile(directory, "pt-br"));
                Overlay(active, ReadFile(directory, culture));
            }
        }

        private static void Overlay(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            if (source == null) return;
            foreach (var entry in source) target[entry.Key] = entry.Value;
        }

        private static Dictionary<string, string> ReadEmbedded(string language)
        {
            using (var stream = typeof(Strings).Assembly.GetManifestResourceStream("RayXDesktop.Language." + language))
                return stream == null ? new Dictionary<string, string>(StringComparer.Ordinal) : ReadXml(stream);
        }

        private static Dictionary<string, string> ReadFile(string directory, string language)
        {
            string path = Path.Combine(directory, language + ".xml");
            try
            {
                using (var stream = File.OpenRead(path))
                    return ReadXml(stream);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException ex) { Warn(language, ex); }
            catch (UnauthorizedAccessException ex) { Warn(language, ex); }
            catch (XmlException ex) { Warn(language, ex); }
            catch (SecurityException ex) { Warn(language, ex); }
            return null;
        }

        private static void Warn(string language, Exception ex)
        {
            Trace.TraceWarning("Cannot read language pack {0}: {1}", language, ex.Message);
        }

        private static Dictionary<string, string> ReadXml(Stream stream)
        {
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            }))
            {
                var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
                if (root == null || root.Name != "translations")
                    throw new XmlException("Expected a translations element.");

                var translations = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var element in root.Elements("text"))
                {
                    string key = (string)element.Attribute("key");
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(element.Value)) continue;
                    if (translations.ContainsKey(key))
                        throw new XmlException("Duplicate translation key: " + key);
                    translations.Add(key, element.Value);
                }
                return translations;
            }
        }

        public static string T(string key)
        {
            string value;
            return active.TryGetValue(key, out value) ? value : key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException)
            {
                string standard;
                return bundled.TryGetValue(key, out standard) ? string.Format(standard, args) : key;
            }
        }
    }
}
