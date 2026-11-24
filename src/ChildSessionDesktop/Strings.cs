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
        private static readonly Dictionary<string, string> French = ReadEmbedded("fr");
        private static Dictionary<string, string> active = English;

        internal static bool IsFrench { get; private set; }

        public static void SetLanguage(string language, string directory = null)
        {
            string tag = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language.Trim();
            if (tag.Length > 64 || !LanguageTag.IsMatch(tag)) tag = "en";

            directory = directory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "languages");
            string[] parts = tag.ToLowerInvariant().Split('-');
            IsFrench = parts[0] == "fr";

            var translations = new Dictionary<string, string>(English, StringComparer.Ordinal);
            Overlay(translations, ReadFile(directory, "en"));
            if (IsFrench) Overlay(translations, French);

            string culture = parts[0];
            if (culture != "en") Overlay(translations, ReadFile(directory, culture));
            for (int i = 1; i < parts.Length; i++)
            {
                culture += "-" + parts[i];
                Overlay(translations, ReadFile(directory, culture));
            }
            active = translations;
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
                if (IsFrench && French.TryGetValue(key, out standard)) return string.Format(standard, args);
                return English.TryGetValue(key, out standard) ? string.Format(standard, args) : key;
            }
        }
    }
}
