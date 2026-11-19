using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChildSessionDesktop
{
    internal static class Strings
    {
        internal static bool IsFrench { get; private set; }

        public static void SetLanguage(string lang)
        {
            IsFrench = string.IsNullOrEmpty(lang)
                ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr"
                : string.Equals(lang, "fr", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, string> en = new Dictionary<string, string>
        {
            { "err_title", "RayXDesktop error" },
            { "startup_fail_title", "RayXDesktop failed to start" },
            { "msg_already_running", "RayXDesktop is already running." },
            { "msg_account_usage", @"Usage: RayXDesktop.exe --account=DOMAIN\User" },
            { "credentials_title", "Sign in to RayXDesktop" },
            { "credentials_account", "Account: {0}" },
            { "credentials_password", "Password" },
            { "credentials_connect", "Connect" },
            { "credentials_cancel", "Cancel" },
            { "status_credentials_cancelled", "Sign-in canceled. Use Reconnect to try again." },
            { "menu_fixed", "Fixed resolution" },
            { "menu_dynamic", "Dynamic resolution" },
            { "menu_about", "About RayXDesktop" },
            { "menu_open_log", "Open log folder" },
            { "menu_check_update", "View Windows builds…" },
            { "about_body", "RayXDesktop {0}\nMaintained by D.Synx\n\nA local Windows Child Session desktop and maintenance tools.\nOriginal RayXDesktop project by raydoomed (MIT)." },
            { "res_your_screen", "{0} × {1} (your screen, recommended)" },
            { "res_confirm_title", "Change resolution" },
            { "res_confirm_body", "Change the virtual desktop to {0} × {1} now?\n\n• Programs already open in the virtual desktop are kept, not closed\n• But any running fullscreen game may exit" },
            { "close_confirm_title", "Close virtual desktop" },
            { "close_confirm_body", "The virtual desktop is still connected and running programs may have unsaved work. Close anyway?" },
            { "res_not_connected", "Not connected yet; cannot change resolution." },
            { "res_fail", "Failed to change resolution: " },
            { "status_connected", "Connected" },
            { "status_connecting", "Connecting to the local Child Session…" },
            { "status_connecting_win", "Connecting to the local Windows Child Session…" },
            { "status_waiting", "Waiting for Windows to create the Child Session…" },
            { "status_not_connected", "Not connected" },
            { "status_timeout", "Connection timed out. Make sure Child Sessions are enabled on this computer, then sign out and back in." },
            { "status_disabled_missing_helper", "Child Sessions are not enabled on this computer; the elevation helper EnableChildSessions.exe is missing." },
            { "status_requesting", "Requesting administrator to enable Child Sessions. Sign out and back in after enabling; if a credentials prompt appears, cancel it and do not enter an empty password." },
            { "status_elevation_canceled", "Administrator authorization was canceled; Child Sessions are still not enabled." },
            { "status_launch_helper_fail", "Could not launch the elevation helper: " },
            { "status_disconnected_local", "The RDP connection was disconnected locally (0x00000001); this is a status code, not an error code." },
            { "menu_reconnect", "Reconnect" },
            { "status_reconnect_failed", "Auto-reconnect failed several times; use the Reconnect button to retry." },
            { "status_disconnected", "Child Session disconnected, error code 0x{0:X8}" },
            { "status_disconnected_acct", " (account restricted). Check the Windows account status and sign-in policy; if you just enabled Child Sessions, sign out and back in and try again." },
            { "status_fatal", "Fatal RDP error 0x{0:X8}" },
            { "status_logon_error", "Child Session logon error 0x{0:X8}" },
            { "status_continuing", "Windows is continuing to build the Child Session…" },
            { "status_state", "RDP control state: " },
            { "status_read_fail", "Failed to read connection state: " },
            { "msg_connect_fail", "Connection failed: " },
            { "msg_check_fail", "Could not check the Windows Child Session state, system error code {0}. Make sure this Windows version supports remote desktop Child Sessions." },
            { "btn_fullscreen", "Fullscreen" }, { "btn_display", "Display" }, { "btn_resolution", "Resolution" },
            { "btn_exit", "Exit" }, { "btn_more", "More" }, { "btn_collapse", "Collapse" }, { "btn_drag", "Drag" },
            { "tip_display_dynamic", "Display · Dynamic" }, { "tip_display_fixed", "Display · Fixed" },
            { "tip_expand", "Expand" }, { "tip_collapse", "Collapse" },
        };

        private static readonly Dictionary<string, string> fr = new Dictionary<string, string>
        {
            { "err_title", "Erreur RayXDesktop" },
            { "startup_fail_title", "Échec du démarrage de RayXDesktop" },
            { "msg_already_running", "RayXDesktop est déjà ouvert." },
            { "msg_account_usage", @"Usage : RayXDesktop.exe --account=DOMAINE\Utilisateur" },
            { "credentials_title", "Connexion à RayXDesktop" },
            { "credentials_account", "Compte : {0}" },
            { "credentials_password", "Mot de passe" },
            { "credentials_connect", "Se connecter" },
            { "credentials_cancel", "Annuler" },
            { "status_credentials_cancelled", "Connexion annulée. Utilise Reconnecter pour réessayer." },
            { "menu_fixed", "Résolution fixe" },
            { "menu_dynamic", "Résolution dynamique" },
            { "menu_about", "À propos de RayXDesktop" },
            { "menu_open_log", "Ouvrir le dossier des journaux" },
            { "menu_check_update", "Voir les builds Windows…" },
            { "menu_reconnect", "Reconnecter" },
            { "about_body", "RayXDesktop {0}\nMaintenu par D.Synx\n\nBureau de session enfant Windows et outils de maintenance.\nProjet RayXDesktop d’origine par raydoomed (MIT)." },
            { "res_your_screen", "{0} × {1} (ton écran)" },
            { "res_confirm_title", "Changer la résolution" },
            { "res_confirm_body", "Appliquer la résolution {0} × {1} au bureau ?" },
            { "close_confirm_title", "Fermer le bureau" },
            { "close_confirm_body", "Le bureau est encore connecté et des documents peuvent être ouverts. Fermer cette session ?" },
            { "res_not_connected", "Connecte le bureau avant de changer la résolution." },
            { "res_fail", "Impossible de changer la résolution : " },
            { "status_connected", "Connecté" },
            { "status_connecting", "Connexion au bureau local…" },
            { "status_connecting_win", "Connexion à la session enfant Windows…" },
            { "status_waiting", "En attente de la session Windows…" },
            { "status_not_connected", "Déconnecté" },
            { "status_timeout", "Le délai de connexion a expiré. Vérifie que les sessions enfants sont activées." },
            { "status_disconnected_local", "Connexion RDP fermée localement." },
            { "status_reconnect_failed", "La reconnexion a échoué. Utilise le bouton Reconnecter." },
            { "status_disconnected", "Session déconnectée (0x{0:X8})" },
            { "status_disconnected_acct", " Vérifie les autorisations du compte Windows." },
            { "status_fatal", "Erreur RDP (0x{0:X8})" },
            { "status_logon_error", "Erreur de connexion Windows (0x{0:X8})" },
            { "status_continuing", "Windows termine l’ouverture de la session…" },
            { "status_state", "État RDP : " },
            { "status_read_fail", "Impossible de lire l’état de connexion : " },
            { "msg_connect_fail", "Échec de la connexion : " },
            { "msg_check_fail", "Impossible de vérifier les sessions enfants Windows (erreur {0})." },
            { "btn_fullscreen", "Plein écran" },
            { "btn_display", "Affichage" },
            { "btn_resolution", "Résolution" },
            { "btn_exit", "Fermer la session" },
            { "btn_more", "Plus d’options" },
            { "btn_collapse", "Réduire" },
            { "btn_drag", "Déplacer la barre" },
            { "tip_display_dynamic", "Affichage dynamique" },
            { "tip_display_fixed", "Affichage fixe" },
            { "tip_expand", "Afficher la barre" },
            { "tip_collapse", "Réduire la barre" },
        };

        public static string T(string key)
        {
            string s;
            if (IsFrench && fr.TryGetValue(key, out s)) return s;
            return en.TryGetValue(key, out s) ? s : key;
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
