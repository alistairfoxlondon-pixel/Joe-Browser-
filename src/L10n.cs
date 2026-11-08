using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoeBrowser
{
    /// <summary>
    /// Interface language. Codes are only ever used as storage keys: every picker shows a
    /// circular country flag plus the full language name, never "en / de / ru" abbreviations.
    /// </summary>
    internal static class L10n
    {
        public sealed class UiLang
        {
            public string Key;
            public string Flag;
            public string English;
            public string Native;
            public UiLang(string key, string flag, string english, string native)
            {
                Key = key; Flag = flag; English = english; Native = native;
            }
            public string Label { get { return Native; } }
        }

        public static readonly UiLang[] Languages =
        {
            new UiLang("en", "US", "English",            "English"),
            new UiLang("de", "DE", "German",             "Deutsch"),
            new UiLang("ru", "RU", "Russian",            "Русский"),
            new UiLang("es", "ES", "Spanish",            "Español"),
            new UiLang("fr", "FR", "French",             "Français"),
            new UiLang("pt", "BR", "Portuguese",         "Português"),
            new UiLang("it", "IT", "Italian",            "Italiano"),
            new UiLang("nl", "NL", "Dutch",              "Nederlands"),
            new UiLang("pl", "PL", "Polish",             "Polski"),
            new UiLang("tr", "TR", "Turkish",            "Türkçe"),
            new UiLang("uk", "UA", "Ukrainian",          "Українська"),
            new UiLang("ar", "SA", "Arabic",             "العربية"),
            new UiLang("hi", "IN", "Hindi",              "हिन्दी"),
            new UiLang("zh", "CN", "Chinese (Simplified)","简体中文"),
            new UiLang("ja", "JP", "Japanese",           "日本語"),
            new UiLang("ko", "KR", "Korean",             "한국어"),
            new UiLang("id", "ID", "Indonesian",         "Bahasa Indonesia"),
            new UiLang("vi", "VN", "Vietnamese",         "Tiếng Việt")
        };

        public static string Current { get; private set; } = "en";
        public static event Action Changed;

        public static void Set(string key)
        {
            /* A saved preference may name a language with no table - an older build offered
               three of them, and a hand-edited settings.json can say anything. Falling back to
               English is right; leaving a dangling key in Current is what made T() return the
               raw "nav.profiles" identifiers.
               It also matters that a failed Set does not raise Changed: every listener would
               rebuild the whole interface for nothing. */
            if (!IsTranslated(key)) key = "en";
            if (Current == key) return;
            Current = key;
            var h = Changed;
            if (h != null) { try { h(); } catch { } }
        }

        /// <summary>
        /// True when this language actually has a translation table. The picker used to offer
        /// three languages (Hindi, Indonesian, Vietnamese) that had no strings at all: choosing
        /// one closed the picker, saved the preference and left every label in English with no
        /// explanation. Offering only what is translated is the honest list.
        /// </summary>
        public static bool IsTranslated(string key)
        {
            // Reading _tables here is fine from any call site: it is initialised by the static
            // constructor before any public member can run, wherever it is declared.
            return !string.IsNullOrEmpty(key) && _tables.ContainsKey(key);
        }

        /// <summary>
        /// Share of the interface this language actually covers, 0-100. Every table is a work in
        /// progress and untranslated keys fall back to English, so a language that covers a
        /// third of the app looks broken rather than translated unless the picker says so.
        /// </summary>
        public static int Coverage(string key)
        {
            if (_english.Count == 0) return 0;
            Dictionary<string, string> table;
            if (!_tables.TryGetValue(key ?? "", out table) || table == null) return 0;
            int have = 0;
            foreach (string k in _english.Keys) if (table.ContainsKey(k)) have++;
            return (int)Math.Round(100.0 * have / _english.Count);
        }

        public static bool IsKnown(string key)
        {
            foreach (var l in Languages) if (l.Key == key) return true;
            return false;
        }

        public static UiLang Find(string key)
        {
            foreach (var l in Languages) if (l.Key == key) return l;
            return Languages[0];
        }

        public static string FlagOf(string key) { return Find(key).Flag; }

        /// <summary>Full interface language name, e.g. "German (Deutsch)".</summary>
        public static string NameOf(string key)
        {
            var l = Find(key);
            return l.Native.Equals(l.English, StringComparison.OrdinalIgnoreCase)
                ? l.English
                : l.English + " (" + l.Native + ")";
        }

        public static string T(string key)
        {
            string v;
            Dictionary<string, string> table;
            if (_tables.TryGetValue(Current, out table) && table.TryGetValue(key, out v)) return v;
            if (_english.TryGetValue(key, out v)) return v;
            return key;
        }

        private static Dictionary<string, string> Parse(string block)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in (block ?? "").Split('\n'))
            {
                string line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line[0] == '#') continue;
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }

        private static readonly Dictionary<string, string> _english = Parse(En);
        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", Parse(En) }, { "de", Parse(De) }, { "ru", Parse(Ru) },
                { "es", Parse(Es) }, { "fr", Parse(Fr) }, { "pt", Parse(Pt) },
                { "it", Parse(It) }, { "nl", Parse(Nl) }, { "pl", Parse(Pl) },
                { "tr", Parse(Tr) }, { "uk", Parse(Uk) }, { "ar", Parse(Ar) },
                { "zh", Parse(Zh) }, { "ja", Parse(Ja) }, { "ko", Parse(Ko) }
            };

        private const string En = @"
nav.profiles=Profiles
nav.settings=Settings
nav.tools=Tools
nav.about=About
nav.proxies=Proxies
nav.extensions=Extensions
nav.browsers=Browsers
nav.plan=Plan
nav.workspace=Workspace
nav.system=System
theme.night=Night Mode
theme.day=Day Mode
header.profiles=Profiles
header.settings=Settings
header.tools=Tools & data
header.about=About
header.proxies=Proxies
header.extensions=Extensions
header.browsers=Browsers
header.plan=Plan
sub.settings=Browser, storage and start page
sub.tools=Batch operations, cookies, proxies and extensions
sub.about=Private browser profiles, built for serious workflows
sub.proxies=Save a proxy once, check it, then use it in any profile
sub.extensions=Built-in tools and extensions you add yourself
sub.browsers=What is installed on this PC, and one click to add the rest
sub.plan=Unlimited profiles for $1 a month, paid in crypto
btn.new=New profile
btn.batch=Batch create
btn.save=Save changes
btn.sort=Sort
btn.search=Search profiles
btn.create=Create profile
btn.openall=Open all
btn.stop=Stop
btn.duplicate=Duplicate
btn.delete=Delete
btn.cancel=Cancel
btn.clear=Clear selection
btn.open=Open
btn.browse=Browse
btn.detect=Auto-detect
btn.download=Download Chromium
btn.opendata=Open data folder
btn.test=Test connection
btn.create.many=Create
btn.import=Import
btn.export=Export
btn.testall=Test
btn.restore=Restore
btn.view=View
btn.clean=Clean
btn.check=Check
btn.close=Close
empty.title=No profiles yet
empty.sub=Create your first identity. Each one is a separate, private browser.
empty.nomatch=No matching profiles
empty.nomatch.sub=Try another name, group, tag, operating system, language or proxy host.
empty.action=Create profile
footer.local=Local-first · private
footer.browsers=browsers running
footer.browser=browser running
footer.version=Version 1.1.0
set.browser=BROWSER ENGINE
set.start=START PAGE
set.storage=STORAGE
set.interface=INTERFACE LANGUAGE
set.tools=PROFILE TOOLS
set.browser.hint=Leave empty to use the Chromium browser installed on this PC.
set.start.hint=Opened in a new tab every time a profile starts.
set.storage.hint=Everything stays on this device. No account, no cloud.
set.interface.hint=Shown with a flag and full name. Changes the labels, not your fingerprint.
set.tools.hint=Applied to every new profile. Each profile can override them.
set.ext=Load built-in extensions
set.trash=Keep deleted profiles in the recycle bin
set.cext=Load extensions from the library
set.group=DEFAULT GROUP
set.group.hint=e.g. Clients, Work, Testing
set.workspace=WORKSPACE
set.row.detected=Detected browser
set.row.path=Executable path
set.row.start=Start page
set.row.group=Default group
set.row.lang=Interface language
set.row.data=Data folder
set.row.cache=Cached files
set.row.report=Found a problem?
btn.report=Report it
tools.report=Report a problem
tools.report.desc=Message the developer directly on Telegram.
set.row.build=Portable Chromium
set.account=ACCOUNT & SECURITY
set.row.signed=Signed in as
set.row.google=Google client ID
set.row.gsecret=Google client secret
set.row.security=Profile lock and 2FA
set.row.signout=Sign out
set.google.hint=Paste the OAuth client ID from your Google Cloud console (Desktop app). Required only for Continue with Google.
btn.security=Manage
acct.signedout=Signed out
about.features=FEATURES
about.f1=Unlimited profiles
about.f1d=Separate cookies, storage and sessions.
about.f2=Coherent fingerprints
about.f2d=Canvas, WebGL, timezone and locale aligned.
about.f3=Local and encrypted
about.f3d=No account, no cloud. Every byte stays local.
about.f4=Proxy library
about.f4d=Save a proxy once, check it, reuse it anywhere.
about.f5=Every device
about.f5d=Android, iPhone, iPad, Windows, macOS and Linux.
about.f6=Live proxy switch
about.f6d=Change the exit IP without closing the tab.
about.f7=Extensions
about.f7d=Import from the Chrome Web Store or a folder.
about.f8=Leak protection
about.f8d=WebRTC, DNS and timezone stay behind the proxy.
about.shortcuts=KEYBOARD SHORTCUTS
about.workspace=THIS WORKSPACE
about.w1=Profiles on this device
about.w2=Saved proxies
about.w3=Extensions loaded
tab.general=General
tab.fingerprint=Fingerprint
tab.privacy=Privacy
tab.proxy=Proxy
tab.tools=Tools
ed.new=New profile
ed.edit=Edit profile
ed.name=PROFILE NAME
ed.start=START PAGE
ed.browser=BROWSER
ed.path=CUSTOM PATH (OPTIONAL)
ed.notes=NOTES
ed.group=GROUP / FOLDER
ed.tags=TAGS (COMMA SEPARATED)
ed.status=STATUS
ed.save=Save changes
ed.create=Create profile
ed.cancel=Cancel
ed.os=DEVICE TARGET
ed.screen=SCREEN
ed.cores=CPU CORES
ed.memory=MEMORY
ed.timezone=TIMEZONE
ed.language=LANGUAGE
ed.gpu=WEBGL RENDERER
ed.regen=New fingerprint
ed.webrtc=WEBRTC LEAK
ed.geo=GEOLOCATION
ed.lat=LATITUDE
ed.lng=LONGITUDE
ed.ua=USER AGENT
ed.autoua=Generate from device
ed.canvas=Canvas noise
ed.audio=Audio noise
ed.fonts=Font noise
ed.battery=Battery reading
ed.dnt=Do Not Track
ed.automatch=Match locale to connection
ed.ptype=PROXY TYPE
ed.host=HOST
ed.port=PORT
ed.user=USERNAME
ed.pass=PASSWORD
ed.ext=BUILT-IN EXTENSIONS
ed.humanpaste=Paste as human
ed.humanspeed=TYPING SPEED
ed.proxywatch=Proxy rotation guard
ed.watchinterval=CHECK INTERVAL
ed.rightclick=Disable right click
ed.rightshift=Allow Shift to restore the menu
ed.customext=EXTRA EXTENSION FOLDERS
ed.customext.hint=One absolute folder per line. These are unpacked extensions you built yourself.
ed.openfolder=Open extensions folder
card.open=Open profile
card.focus=Focus window
card.close=Close browser
card.edit=Edit profile
card.proxy=Set proxy
card.report=Fingerprint report
card.cookiesin=Import cookies…
card.cookiesout=Export cookies…
card.folder=Open profile folder
card.select=Add to selection
card.unselect=Remove from selection
card.duplicate=Duplicate
card.delete=Delete profile
tools.batch=Batch create
tools.batch.desc=Create many isolated profiles at once, optionally with a proxy per line.
tools.import=Import profiles
tools.import.desc=Load profiles from a Joe Browser .json export.
tools.export=Export profiles
tools.export.desc=Save every profile as a portable .json backup (proxy secrets excluded).
tools.proxy=Proxy batch test
tools.proxy.desc=Paste a list of proxies and check IP, country, latency and failures.
tools.cookies=Cookie manager
tools.cookies.desc=Import or export cookies for any running browser session.
tools.ext=Extension folder
tools.ext.desc=Open the folder with the original built-in extensions.
tools.trash=Recycle bin
tools.trash.desc=Restore profiles that were deleted by mistake.
tools.shortcuts=Keyboard shortcuts
tools.shortcuts.desc=Every shortcut available in the profile list.
tools.clean=Clear browser caches
tools.clean.desc=Free disk space by emptying cache folders without touching cookies or logins.
tools.net=Network diagnostics
tools.net.desc=Exit IP, resolver and clock for this PC or any saved proxy, checked by three services.
";

        private const string De = @"
nav.profiles=Profile
nav.settings=Einstellungen
nav.tools=Werkzeuge
nav.about=Über
theme.night=Nachtmodus
theme.day=Tagmodus
header.profiles=Profile
header.settings=Einstellungen
header.tools=Werkzeuge & Daten
header.about=Über
sub.settings=Browser, Speicher und Startseite
sub.tools=Stapelaktionen, Cookies, Proxys und Erweiterungen
btn.new=Neues Profil
btn.batch=Stapel erstellen
btn.save=Änderungen speichern
btn.sort=Sortieren
btn.search=Profile suchen
btn.create=Profil erstellen
btn.openall=Alle öffnen
btn.stop=Stopp
btn.duplicate=Duplizieren
btn.delete=Löschen
btn.cancel=Abbrechen
btn.open=Öffnen
btn.browse=Durchsuchen
btn.detect=Automatisch erkennen
btn.download=Chromium herunterladen
btn.opendata=Datenordner öffnen
btn.test=Verbindung testen
empty.title=Noch keine Profile
empty.sub=Erstelle deine erste Identität. Jede ist ein eigener, privater Browser.
empty.nomatch=Keine passenden Profile
empty.nomatch.sub=Versuche einen anderen Namen, eine Gruppe, ein Tag, System, Sprache oder Proxy.
empty.action=Profil erstellen
footer.local=Lokal & privat
footer.browsers=Browser laufen
footer.browser=Browser läuft
set.browser=BROWSER-ENGINE
set.start=STARTSEITE
set.storage=SPEICHER
set.interface=OBERFLÄCHENSPRACHE
set.tools=PROFIL-WERKZEUGE
set.interface.hint=Mit Flagge und vollem Namen. Ändert nur Beschriftungen, nicht den Fingerabdruck.
about.features=FUNKTIONEN
about.f1=Unbegrenzte Profile
about.f2=Kohärente Fingerabdrücke
about.f3=Lokal und verschlüsselt
tab.general=Allgemein
tab.fingerprint=Fingerabdruck
tab.privacy=Datenschutz
tab.proxy=Proxy
tab.tools=Werkzeuge
ed.new=Neues Profil
ed.edit=Profil bearbeiten
ed.name=PROFILNAME
ed.start=STARTSEITE
ed.browser=BROWSER
ed.path=EIGENER PFAD (OPTIONAL)
ed.notes=NOTIZEN
ed.group=GRUPPE / ORDNER
ed.tags=TAGS (MIT KOMMA GETRENNT)
ed.status=STATUS
ed.create=Profil erstellen
ed.save=Änderungen speichern
ed.os=GERÄTEZIEL
ed.screen=BILDSCHIRM
ed.cores=CPU-KERNE
ed.memory=ARBEITSSPEICHER
ed.timezone=ZEITZONE
ed.language=SPRACHE
ed.gpu=WEBGL-RENDERER
ed.regen=Neuer Fingerabdruck
ed.webrtc=WEBRTC-LECK
ed.geo=GEOLOKALISIERUNG
ed.lat=BREITENGRAD
ed.lng=LÄNGENGRAD
ed.ua=USER-AGENT
ed.autoua=Aus Gerät erzeugen
ed.canvas=Canvas-Rauschen
ed.audio=Audio-Rauschen
ed.fonts=Schrift-Rauschen
ed.battery=Akkustand
ed.dnt=Do Not Track
ed.automatch=Region an Verbindung anpassen
ed.ptype=PROXY-TYP
ed.host=HOST
ed.port=PORT
ed.user=BENUTZERNAME
ed.pass=PASSWORT
ed.ext=EIGENE ERWEITERUNGEN
ed.humanpaste=Menschlich einfügen
ed.proxywatch=Proxy-Wechsel-Wächter
ed.rightclick=Rechtsklick deaktivieren
ed.customext=WEITERE ERWEITERUNGSORDNER
ed.openfolder=Erweiterungsordner öffnen
card.edit=Profil bearbeiten
card.proxy=Proxy setzen
card.report=Fingerabdruck-Bericht
card.duplicate=Duplizieren
card.delete=Profil löschen
";

        private const string Ru = @"
nav.profiles=Профили
nav.settings=Настройки
nav.tools=Инструменты
nav.about=О программе
theme.night=Ночной режим
theme.day=Дневной режим
header.profiles=Профили
header.settings=Настройки
header.tools=Инструменты и данные
header.about=О программе
sub.settings=Браузер, хранилище и стартовая страница
sub.tools=Массовые операции, cookies, прокси и расширения
btn.new=Новый профиль
btn.batch=Создать пакет
btn.save=Сохранить
btn.sort=Сортировка
btn.search=Поиск профилей
btn.create=Создать профиль
btn.openall=Открыть все
btn.stop=Остановить
btn.duplicate=Дублировать
btn.delete=Удалить
btn.cancel=Отмена
btn.open=Открыть
btn.browse=Обзор
btn.detect=Определить
btn.download=Скачать Chromium
btn.opendata=Открыть папку данных
btn.test=Проверить соединение
empty.title=Профилей пока нет
empty.sub=Создайте первую личность. Каждая — отдельный приватный браузер.
empty.nomatch=Ничего не найдено
empty.nomatch.sub=Попробуйте другое имя, группу, тег, систему, язык или прокси.
empty.action=Создать профиль
footer.local=Локально и приватно
footer.browsers=браузеров запущено
footer.browser=браузер запущен
set.browser=БРАУЗЕРНЫЙ ДВИЖОК
set.start=СТАРТОВАЯ СТРАНИЦА
set.storage=ХРАНИЛИЩЕ
set.interface=ЯЗЫК ИНТЕРФЕЙСА
set.tools=ИНСТРУМЕНТЫ ПРОФИЛЯ
about.features=ВОЗМОЖНОСТИ
about.f1=Безлимитные профили
about.f2=Согласованные отпечатки
about.f3=Локально и с шифрованием
tab.general=Общее
tab.fingerprint=Отпечаток
tab.privacy=Приватность
tab.proxy=Прокси
tab.tools=Инструменты
ed.new=Новый профиль
ed.edit=Изменить профиль
ed.name=ИМЯ ПРОФИЛЯ
ed.start=СТАРТОВАЯ СТРАНИЦА
ed.browser=БРАУЗЕР
ed.path=СВОЙ ПУТЬ (НЕОБЯЗАТЕЛЬНО)
ed.notes=ЗАМЕТКИ
ed.group=ГРУППА / ПАПКА
ed.tags=ТЕГИ (ЧЕРЕЗ ЗАПЯТУЮ)
ed.status=СТАТУС
ed.create=Создать профиль
ed.save=Сохранить
ed.os=ЦЕЛЕВОЕ УСТРОЙСТВО
ed.screen=ЭКРАН
ed.cores=ЯДРА CPU
ed.memory=ПАМЯТЬ
ed.timezone=ЧАСОВОЙ ПОЯС
ed.language=ЯЗЫК
ed.gpu=WEBGL-РЕНДЕРЕР
ed.regen=Новый отпечаток
ed.webrtc=УТЕЧКА WEBRTC
ed.geo=ГЕОЛОКАЦИЯ
ed.lat=ШИРОТА
ed.lng=ДОЛГОТА
ed.ua=USER-AGENT
ed.autoua=Создавать из устройства
ed.canvas=Шум Canvas
ed.audio=Шум Audio
ed.fonts=Шум шрифтов
ed.battery=Уровень батареи
ed.dnt=Do Not Track
ed.automatch=Подгонять регион под соединение
ed.ptype=ТИП ПРОКСИ
ed.host=ХОСТ
ed.port=ПОРТ
ed.user=ЛОГИН
ed.pass=ПАРОЛЬ
ed.ext=ВСТРОЕННЫЕ РАСШИРЕНИЯ
ed.humanpaste=Вставка как человек
ed.proxywatch=Контроль смены прокси
ed.rightclick=Отключить правый клик
ed.customext=ДОПОЛНИТЕЛЬНЫЕ ПАПКИ РАСШИРЕНИЙ
ed.openfolder=Открыть папку расширений
card.edit=Изменить профиль
card.proxy=Задать прокси
card.report=Отчёт об отпечатке
card.duplicate=Дублировать
card.delete=Удалить профиль
";

        private const string Es = @"
nav.profiles=Perfiles
nav.settings=Ajustes
nav.tools=Herramientas
nav.about=Acerca de
theme.night=Modo nocturno
theme.day=Modo diurno
header.profiles=Perfiles
header.settings=Ajustes
header.tools=Herramientas y datos
header.about=Acerca de
btn.new=Nuevo perfil
btn.batch=Crear en lote
btn.save=Guardar cambios
btn.sort=Ordenar
btn.search=Buscar perfiles
btn.create=Crear perfil
btn.openall=Abrir todos
btn.stop=Detener
btn.duplicate=Duplicar
btn.delete=Eliminar
btn.cancel=Cancelar
btn.open=Abrir
empty.title=Aún no hay perfiles
empty.sub=Crea tu primera identidad. Cada una es un navegador privado e independiente.
empty.nomatch=Sin coincidencias
empty.action=Crear perfil
footer.local=Local y privado
set.browser=MOTOR DEL NAVEGADOR
set.start=PÁGINA DE INICIO
set.storage=ALMACENAMIENTO
set.interface=IDIOMA DE LA INTERFAZ
set.tools=HERRAMIENTAS DEL PERFIL
about.features=FUNCIONES
tab.general=General
tab.fingerprint=Huella digital
tab.privacy=Privacidad
tab.proxy=Proxy
tab.tools=Herramientas
ed.new=Nuevo perfil
ed.edit=Editar perfil
ed.name=NOMBRE DEL PERFIL
ed.start=PÁGINA DE INICIO
ed.browser=NAVEGADOR
ed.path=RUTA PERSONALIZADA (OPCIONAL)
ed.notes=NOTAS
ed.group=GRUPO / CARPETA
ed.tags=ETIQUETAS (SEPARADAS POR COMAS)
ed.status=ESTADO
ed.create=Crear perfil
ed.save=Guardar cambios
ed.os=DISPOSITIVO DESTINO
ed.screen=PANTALLA
ed.cores=NÚCLEOS DE CPU
ed.memory=MEMORIA
ed.timezone=ZONA HORARIA
ed.language=IDIOMA
ed.gpu=RENDERIZADOR WEBGL
ed.regen=Nueva huella
ed.webrtc=FUGA WEBRTC
ed.geo=GEOLOCALIZACIÓN
ed.ua=AGENTE DE USUARIO
ed.autoua=Generar desde el dispositivo
ed.canvas=Ruido de Canvas
ed.audio=Ruido de Audio
ed.fonts=Ruido de fuentes
ed.battery=Nivel de batería
ed.dnt=No rastrear
ed.automatch=Ajustar región a la conexión
ed.ptype=TIPO DE PROXY
ed.host=HOST
ed.port=PUERTO
ed.user=USUARIO
ed.pass=CONTRASEÑA
ed.ext=EXTENSIONES INTEGRADAS
ed.humanpaste=Pegar como humano
ed.proxywatch=Vigilancia de rotación de proxy
ed.rightclick=Desactivar clic derecho
card.edit=Editar perfil
card.proxy=Configurar proxy
card.report=Informe de huella
card.duplicate=Duplicar
card.delete=Eliminar perfil
";

        private const string Fr = @"
nav.profiles=Profils
nav.settings=Paramètres
nav.tools=Outils
nav.about=À propos
theme.night=Mode nuit
theme.day=Mode jour
header.profiles=Profils
header.settings=Paramètres
header.tools=Outils et données
header.about=À propos
btn.new=Nouveau profil
btn.batch=Créer en lot
btn.save=Enregistrer
btn.sort=Trier
btn.search=Rechercher des profils
btn.create=Créer le profil
btn.openall=Tout ouvrir
btn.stop=Arrêter
btn.duplicate=Dupliquer
btn.delete=Supprimer
btn.cancel=Annuler
btn.open=Ouvrir
empty.title=Aucun profil
empty.sub=Créez votre première identité. Chacune est un navigateur privé distinct.
empty.nomatch=Aucun résultat
empty.action=Créer un profil
footer.local=Local et privé
set.browser=MOTEUR DU NAVIGATEUR
set.start=PAGE DE DÉMARRAGE
set.storage=STOCKAGE
set.interface=LANGUE DE L'INTERFACE
set.tools=OUTILS DU PROFIL
about.features=FONCTIONNALITÉS
tab.general=Général
tab.fingerprint=Empreinte
tab.privacy=Confidentialité
tab.proxy=Proxy
tab.tools=Outils
ed.new=Nouveau profil
ed.edit=Modifier le profil
ed.name=NOM DU PROFIL
ed.start=PAGE DE DÉMARRAGE
ed.browser=NAVIGATEUR
ed.path=CHEMIN PERSONNALISÉ (FACULTATIF)
ed.notes=NOTES
ed.group=GROUPE / DOSSIER
ed.tags=ÉTIQUETTES (SÉPARÉES PAR DES VIRGULES)
ed.status=STATUT
ed.create=Créer le profil
ed.save=Enregistrer
ed.os=APPAREIL CIBLE
ed.screen=ÉCRAN
ed.cores=CŒURS CPU
ed.memory=MÉMOIRE
ed.timezone=FUSEAU HORAIRE
ed.language=LANGUE
ed.gpu=RENDU WEBGL
ed.regen=Nouvelle empreinte
ed.webrtc=FUITE WEBRTC
ed.geo=GÉOLOCALISATION
ed.ua=USER-AGENT
ed.autoua=Générer depuis l'appareil
ed.canvas=Bruit Canvas
ed.audio=Bruit Audio
ed.fonts=Bruit des polices
ed.battery=Niveau de batterie
ed.dnt=Ne pas suivre
ed.automatch=Adapter la région à la connexion
ed.ptype=TYPE DE PROXY
ed.host=HÔTE
ed.port=PORT
ed.user=IDENTIFIANT
ed.pass=MOT DE PASSE
ed.ext=EXTENSIONS INTÉGRÉES
ed.humanpaste=Coller comme un humain
ed.proxywatch=Surveillance de rotation du proxy
ed.rightclick=Désactiver le clic droit
card.edit=Modifier le profil
card.proxy=Définir le proxy
card.report=Rapport d'empreinte
card.duplicate=Dupliquer
card.delete=Supprimer le profil
";

        private const string Pt = @"
nav.profiles=Perfis
nav.settings=Configurações
nav.tools=Ferramentas
nav.about=Sobre
theme.night=Modo noturno
theme.day=Modo claro
header.profiles=Perfis
header.settings=Configurações
header.tools=Ferramentas e dados
header.about=Sobre
btn.new=Novo perfil
btn.batch=Criar em lote
btn.save=Salvar alterações
btn.sort=Ordenar
btn.search=Buscar perfis
btn.create=Criar perfil
btn.openall=Abrir todos
btn.stop=Parar
btn.duplicate=Duplicar
btn.delete=Excluir
btn.cancel=Cancelar
btn.open=Abrir
empty.title=Nenhum perfil ainda
empty.sub=Crie sua primeira identidade. Cada uma é um navegador privado separado.
empty.nomatch=Nenhum resultado
empty.action=Criar perfil
footer.local=Local e privado
set.browser=MOTOR DO NAVEGADOR
set.start=PÁGINA INICIAL
set.storage=ARMAZENAMENTO
set.interface=IDIOMA DA INTERFACE
set.tools=FERRAMENTAS DO PERFIL
about.features=RECURSOS
tab.general=Geral
tab.fingerprint=Impressão digital
tab.privacy=Privacidade
tab.proxy=Proxy
tab.tools=Ferramentas
ed.new=Novo perfil
ed.edit=Editar perfil
ed.name=NOME DO PERFIL
ed.start=PÁGINA INICIAL
ed.browser=NAVEGADOR
ed.path=CAMINHO PERSONALIZADO (OPCIONAL)
ed.notes=NOTAS
ed.group=GRUPO / PASTA
ed.tags=ETIQUETAS (SEPARADAS POR VÍRGULA)
ed.status=STATUS
ed.create=Criar perfil
ed.save=Salvar alterações
ed.os=DISPOSITIVO ALVO
ed.screen=TELA
ed.cores=NÚCLEOS DA CPU
ed.memory=MEMÓRIA
ed.timezone=FUSO HORÁRIO
ed.language=IDIOMA
ed.gpu=RENDERIZADOR WEBGL
ed.regen=Nova impressão digital
ed.webrtc=VAZAMENTO WEBRTC
ed.geo=GEOLOCALIZAÇÃO
ed.ua=USER-AGENT
ed.autoua=Gerar pelo dispositivo
ed.canvas=Ruído de Canvas
ed.audio=Ruído de Áudio
ed.fonts=Ruído de fontes
ed.battery=Nível da bateria
ed.dnt=Não rastrear
ed.automatch=Ajustar região à conexão
ed.ptype=TIPO DE PROXY
ed.host=HOST
ed.port=PORTA
ed.user=USUÁRIO
ed.pass=SENHA
ed.ext=EXTENSÕES INTEGRADAS
ed.humanpaste=Colar como humano
ed.proxywatch=Monitor de rotação de proxy
ed.rightclick=Desativar botão direito
card.edit=Editar perfil
card.proxy=Definir proxy
card.report=Relatório de impressão digital
card.duplicate=Duplicar
card.delete=Excluir perfil
";

        private const string It = @"
nav.profiles=Profili
nav.settings=Impostazioni
nav.tools=Strumenti
nav.about=Informazioni
theme.night=Modalità notte
theme.day=Modalità giorno
header.profiles=Profili
header.settings=Impostazioni
header.tools=Strumenti e dati
header.about=Informazioni
btn.new=Nuovo profilo
btn.batch=Crea in blocco
btn.save=Salva modifiche
btn.sort=Ordina
btn.search=Cerca profili
btn.create=Crea profilo
btn.openall=Apri tutti
btn.stop=Ferma
btn.duplicate=Duplica
btn.delete=Elimina
btn.cancel=Annulla
btn.open=Apri
empty.title=Nessun profilo
empty.sub=Crea la tua prima identità. Ognuna è un browser privato separato.
empty.nomatch=Nessun risultato
empty.action=Crea profilo
footer.local=Locale e privato
set.browser=MOTORE DEL BROWSER
set.start=PAGINA INIZIALE
set.storage=ARCHIVIAZIONE
set.interface=LINGUA DELL'INTERFACCIA
set.tools=STRUMENTI DEL PROFILO
tab.general=Generale
tab.fingerprint=Impronta
tab.privacy=Privacy
tab.proxy=Proxy
tab.tools=Strumenti
ed.new=Nuovo profilo
ed.edit=Modifica profilo
ed.name=NOME PROFILO
ed.start=PAGINA INIZIALE
ed.browser=BROWSER
ed.notes=NOTE
ed.group=GRUPPO / CARTELLA
ed.tags=TAG (SEPARATI DA VIRGOLA)
ed.status=STATO
ed.create=Crea profilo
ed.save=Salva modifiche
ed.language=LINGUA
ed.timezone=FUSO ORARIO
ed.screen=SCHERMO
ed.cores=CORE CPU
ed.memory=MEMORIA
ed.gpu=RENDERER WEBGL
ed.regen=Nuova impronta
ed.ext=ESTENSIONI INTEGRATE
ed.humanpaste=Incolla come umano
ed.proxywatch=Controllo rotazione proxy
ed.rightclick=Disabilita tasto destro
card.edit=Modifica profilo
card.duplicate=Duplica
card.delete=Elimina profilo
";

        private const string Nl = @"
nav.profiles=Profielen
nav.settings=Instellingen
nav.tools=Hulpmiddelen
nav.about=Over
theme.night=Nachtmodus
theme.day=Dagmodus
header.profiles=Profielen
header.settings=Instellingen
header.tools=Hulpmiddelen en gegevens
header.about=Over
btn.new=Nieuw profiel
btn.batch=Batch aanmaken
btn.save=Wijzigingen opslaan
btn.sort=Sorteren
btn.search=Profielen zoeken
btn.create=Profiel aanmaken
btn.openall=Alles openen
btn.stop=Stoppen
btn.duplicate=Dupliceren
btn.delete=Verwijderen
btn.cancel=Annuleren
btn.open=Openen
empty.title=Nog geen profielen
empty.sub=Maak je eerste identiteit aan. Elk profiel is een aparte, privé browser.
empty.nomatch=Geen resultaten
empty.action=Profiel aanmaken
footer.local=Lokaal en privé
set.browser=BROWSER-ENGINE
set.start=STARTPAGINA
set.storage=OPSLAG
set.interface=INTERFACE TAAL
set.tools=PROFIEL HULPMIDDELEN
tab.general=Algemeen
tab.fingerprint=Vingerafdruk
tab.privacy=Privacy
tab.proxy=Proxy
tab.tools=Hulpmiddelen
ed.new=Nieuw profiel
ed.edit=Profiel bewerken
ed.name=PROFIELNAAM
ed.start=STARTPAGINA
ed.browser=BROWSER
ed.notes=NOTITIES
ed.group=GROEP / MAP
ed.tags=LABELS (KOMMA GESCHEIDEN)
ed.status=STATUS
ed.create=Profiel aanmaken
ed.save=Wijzigingen opslaan
ed.language=TAAL
ed.timezone=Tijdzone
ed.screen=SCHERM
ed.cores=CPU-KERNEN
ed.memory=GEHEUGEN
ed.gpu=WEBGL-RENDERER
ed.regen=Nieuwe vingerafdruk
ed.ext=INGEBOUWDE EXTENSIES
ed.humanpaste=Plakken als mens
ed.proxywatch=Proxy-rotatie bewaker
ed.rightclick=Rechterklik uitschakelen
card.edit=Profiel bewerken
card.duplicate=Dupliceren
card.delete=Profiel verwijderen
";

        private const string Pl = @"
nav.profiles=Profile
nav.settings=Ustawienia
nav.tools=Narzędzia
nav.about=O programie
theme.night=Tryb nocny
theme.day=Tryb dzienny
header.profiles=Profile
header.settings=Ustawienia
header.tools=Narzędzia i dane
header.about=O programie
btn.new=Nowy profil
btn.batch=Utwórz zbiorczo
btn.save=Zapisz zmiany
btn.sort=Sortuj
btn.search=Szukaj profili
btn.create=Utwórz profil
btn.openall=Otwórz wszystkie
btn.stop=Zatrzymaj
btn.duplicate=Duplikuj
btn.delete=Usuń
btn.cancel=Anuluj
btn.open=Otwórz
empty.title=Brak profili
empty.sub=Utwórz pierwszą tożsamość. Każda to osobna, prywatna przeglądarka.
empty.nomatch=Brak wyników
empty.action=Utwórz profil
footer.local=Lokalnie i prywatnie
set.browser=SILNIK PRZEGLĄDARKI
set.start=STRONA STARTOWA
set.storage=PRZECHOWYWANIE
set.interface=JĘZYK INTERFEJSU
set.tools=NARZĘDZIA PROFILU
tab.general=Ogólne
tab.fingerprint=Odcisk
tab.privacy=Prywatność
tab.proxy=Proxy
tab.tools=Narzędzia
ed.new=Nowy profil
ed.edit=Edytuj profil
ed.name=NAZWA PROFILU
ed.start=STRONA STARTOWA
ed.browser=PRZEGLĄDARKA
ed.notes=NOTATKI
ed.group=GRUPA / FOLDER
ed.tags=TAGI (PO PRZECINKU)
ed.status=STATUS
ed.create=Utwórz profil
ed.save=Zapisz zmiany
ed.language=JĘZYK
ed.timezone=STREFA CZASOWA
ed.screen=EKRAN
ed.cores=RDZENIE CPU
ed.memory=PAMIĘĆ
ed.gpu=RENDERER WEBGL
ed.regen=Nowy odcisk
ed.ext=WBUDOWANE ROZSZERZENIA
ed.humanpaste=Wklej jak człowiek
ed.proxywatch=Ochrona przed rotacją proxy
ed.rightclick=Wyłącz prawy przycisk
card.edit=Edytuj profil
card.duplicate=Duplikuj
card.delete=Usuń profil
";

        private const string Tr = @"
nav.profiles=Profiller
nav.settings=Ayarlar
nav.tools=Araçlar
nav.about=Hakkında
theme.night=Gece modu
theme.day=Gündüz modu
header.profiles=Profiller
header.settings=Ayarlar
header.tools=Araçlar ve veriler
header.about=Hakkında
btn.new=Yeni profil
btn.batch=Toplu oluştur
btn.save=Değişiklikleri kaydet
btn.sort=Sırala
btn.search=Profillerde ara
btn.create=Profil oluştur
btn.openall=Tümünü aç
btn.stop=Durdur
btn.duplicate=Çoğalt
btn.delete=Sil
btn.cancel=İptal
btn.open=Aç
empty.title=Henüz profil yok
empty.sub=İlk kimliğinizi oluşturun. Her biri ayrı, özel bir tarayıcıdır.
empty.nomatch=Sonuç bulunamadı
empty.action=Profil oluştur
footer.local=Yerel ve özel
set.browser=TARAYICI MOTORU
set.start=BAŞLANGIÇ SAYFASI
set.storage=DEPOLAMA
set.interface=ARAYÜZ DİLİ
set.tools=PROFİL ARAÇLARI
tab.general=Genel
tab.fingerprint=Parmak izi
tab.privacy=Gizlilik
tab.proxy=Proxy
tab.tools=Araçlar
ed.new=Yeni profil
ed.edit=Profili düzenle
ed.name=PROFİL ADI
ed.start=BAŞLANGIÇ SAYFASI
ed.browser=TARAYICI
ed.notes=NOTLAR
ed.group=GRUP / KLASÖR
ed.tags=ETİKETLER (VİRGÜLLE)
ed.status=DURUM
ed.create=Profil oluştur
ed.save=Değişiklikleri kaydet
ed.language=DİL
ed.timezone=SAAT DİLİMİ
ed.screen=EKRAN
ed.cores=CPU ÇEKİRDEĞİ
ed.memory=BELLEK
ed.gpu=WEBGL İŞLEYİCİ
ed.regen=Yeni parmak izi
ed.ext=YERLEŞİK UZANTILAR
ed.humanpaste=İnsan gibi yapıştır
ed.proxywatch=Proxy rotasyon koruması
ed.rightclick=Sağ tıklamayı kapat
card.edit=Profili düzenle
card.duplicate=Çoğalt
card.delete=Profili sil
";

        private const string Uk = @"
nav.profiles=Профілі
nav.settings=Налаштування
nav.tools=Інструменти
nav.about=Про програму
theme.night=Нічний режим
theme.day=Денний режим
header.profiles=Профілі
header.settings=Налаштування
header.tools=Інструменти та дані
header.about=Про програму
btn.new=Новий профіль
btn.batch=Створити пакет
btn.save=Зберегти
btn.sort=Сортування
btn.search=Пошук профілів
btn.create=Створити профіль
btn.openall=Відкрити всі
btn.stop=Зупинити
btn.duplicate=Дублювати
btn.delete=Видалити
btn.cancel=Скасувати
btn.open=Відкрити
empty.title=Профілів ще немає
empty.sub=Створіть першу особу. Кожна — окремий приватний браузер.
empty.nomatch=Нічого не знайдено
empty.action=Створити профіль
footer.local=Локально й приватно
set.browser=РУШІЙ БРАУЗЕРА
set.start=СТАРТОВА СТОРІНКА
set.storage=СХОВИЩЕ
set.interface=МОВА ІНТЕРФЕЙСУ
set.tools=ІНСТРУМЕНТИ ПРОФІЛЮ
tab.general=Загальне
tab.fingerprint=Відбиток
tab.privacy=Приватність
tab.proxy=Проксі
tab.tools=Інструменти
ed.new=Новий профіль
ed.edit=Редагувати профіль
ed.name=ІМ'Я ПРОФІЛЮ
ed.start=СТАРТОВА СТОРІНКА
ed.browser=БРАУЗЕР
ed.notes=НОТАТКИ
ed.group=ГРУПА / ПАПКА
ed.tags=ТЕГИ (ЧЕРЕЗ КОМУ)
ed.status=СТАТУС
ed.create=Створити профіль
ed.save=Зберегти
ed.language=МОВА
ed.timezone=ЧАСОВИЙ ПОЯС
ed.screen=ЕКРАН
ed.cores=ЯДРА CPU
ed.memory=ПАМ'ЯТЬ
ed.gpu=WEBGL-РЕНДЕРЕР
ed.regen=Новий відбиток
ed.ext=ВБУДОВАНІ РОЗШИРЕННЯ
ed.humanpaste=Вставити як людина
ed.proxywatch=Контроль ротації проксі
ed.rightclick=Вимкнути правий клік
card.edit=Редагувати профіль
card.duplicate=Дублювати
card.delete=Видалити профіль
";

        private const string Ar = @"
nav.profiles=الملفات الشخصية
nav.settings=الإعدادات
nav.tools=الأدوات
nav.about=حول
theme.night=الوضع الليلي
theme.day=الوضع النهاري
header.profiles=الملفات الشخصية
header.settings=الإعدادات
header.tools=الأدوات والبيانات
header.about=حول
btn.new=ملف جديد
btn.batch=إنشاء دفعة
btn.save=حفظ التغييرات
btn.sort=ترتيب
btn.search=بحث في الملفات
btn.create=إنشاء الملف
btn.openall=فتح الكل
btn.stop=إيقاف
btn.duplicate=تكرار
btn.delete=حذف
btn.cancel=إلغاء
btn.open=فتح
empty.title=لا توجد ملفات بعد
empty.sub=أنشئ هويتك الأولى. كل ملف متصفح خاص منفصل.
empty.nomatch=لا نتائج
empty.action=إنشاء ملف
footer.local=محلي وخاص
set.browser=محرك المتصفح
set.start=صفحة البداية
set.storage=التخزين
set.interface=لغة الواجهة
set.tools=أدوات الملف الشخصي
tab.general=عام
tab.fingerprint=البصمة
tab.privacy=الخصوصية
tab.proxy=بروكسي
tab.tools=الأدوات
ed.new=ملف جديد
ed.edit=تعديل الملف
ed.name=اسم الملف الشخصي
ed.start=صفحة البداية
ed.browser=المتصفح
ed.notes=ملاحظات
ed.group=المجموعة / المجلد
ed.tags=الوسوم (بفواصل)
ed.status=الحالة
ed.create=إنشاء الملف
ed.save=حفظ التغييرات
ed.language=اللغة
ed.timezone=المنطقة الزمنية
ed.screen=الشاشة
ed.cores=أنوية المعالج
ed.memory=الذاكرة
ed.gpu=معالج WebGL
ed.regen=بصمة جديدة
ed.ext=الإضافات المدمجة
ed.humanpaste=لصق كإنسان
ed.proxywatch=مراقبة تبديل البروكسي
ed.rightclick=تعطيل الزر الأيمن
card.edit=تعديل الملف
card.duplicate=تكرار
card.delete=حذف الملف
";

        private const string Zh = @"
nav.profiles=配置文件
nav.settings=设置
nav.tools=工具
nav.about=关于
theme.night=夜间模式
theme.day=日间模式
header.profiles=配置文件
header.settings=设置
header.tools=工具与数据
header.about=关于
btn.new=新建配置
btn.batch=批量创建
btn.save=保存更改
btn.sort=排序
btn.search=搜索配置文件
btn.create=创建配置
btn.openall=全部打开
btn.stop=停止
btn.duplicate=复制
btn.delete=删除
btn.cancel=取消
btn.open=打开
btn.browse=浏览
btn.detect=自动检测
btn.test=测试连接
empty.title=暂无配置文件
empty.sub=创建你的第一个身份，每个身份都是独立的隐私浏览器。
empty.nomatch=没有匹配的配置
empty.action=创建配置
footer.local=本地优先 · 隐私
set.browser=浏览器内核
set.start=起始页
set.storage=存储
set.interface=界面语言
set.tools=配置工具
about.features=功能特性
tab.general=常规
tab.fingerprint=指纹
tab.privacy=隐私
tab.proxy=代理
tab.tools=工具
ed.new=新建配置
ed.edit=编辑配置
ed.name=配置名称
ed.start=起始页
ed.browser=浏览器
ed.path=自定义路径（可选）
ed.notes=备注
ed.group=分组 / 文件夹
ed.tags=标签（逗号分隔）
ed.status=状态
ed.create=创建配置
ed.save=保存更改
ed.os=目标设备
ed.screen=屏幕
ed.cores=CPU 核心
ed.memory=内存
ed.timezone=时区
ed.language=语言
ed.gpu=WEBGL 渲染器
ed.regen=生成新指纹
ed.webrtc=WEBRTC 泄漏
ed.geo=地理位置
ed.ua=USER AGENT
ed.autoua=根据设备生成
ed.canvas=Canvas 噪声
ed.audio=Audio 噪声
ed.fonts=字体噪声
ed.battery=电池信息
ed.dnt=请勿追踪
ed.automatch=按连接匹配地区
ed.ptype=代理类型
ed.host=主机
ed.port=端口
ed.user=用户名
ed.pass=密码
ed.ext=内置扩展
ed.humanpaste=拟人粘贴
ed.proxywatch=代理轮换监测
ed.rightclick=禁用右键
ed.customext=附加扩展目录
ed.openfolder=打开扩展目录
card.edit=编辑配置
card.proxy=设置代理
card.report=指纹报告
card.duplicate=复制
card.delete=删除配置
";

        private const string Ja = @"
nav.profiles=プロファイル
nav.settings=設定
nav.tools=ツール
nav.about=情報
theme.night=ナイトモード
theme.day=デイモード
header.profiles=プロファイル
header.settings=設定
header.tools=ツールとデータ
header.about=情報
btn.new=新規プロファイル
btn.batch=一括作成
btn.save=変更を保存
btn.sort=並べ替え
btn.search=プロファイルを検索
btn.create=プロファイルを作成
btn.openall=すべて開く
btn.stop=停止
btn.duplicate=複製
btn.delete=削除
btn.cancel=キャンセル
btn.open=開く
empty.title=プロファイルがありません
empty.sub=最初の ID を作成します。それぞれが独立したプライベートブラウザです。
empty.nomatch=一致する項目なし
empty.action=プロファイルを作成
footer.local=ローカル優先 · プライベート
set.browser=ブラウザエンジン
set.start=スタートページ
set.storage=ストレージ
set.interface=表示言語
set.tools=プロファイルツール
tab.general=全般
tab.fingerprint=フィンガープリント
tab.privacy=プライバシー
tab.proxy=プロキシ
tab.tools=ツール
ed.new=新規プロファイル
ed.edit=プロファイルを編集
ed.name=プロファイル名
ed.start=スタートページ
ed.browser=ブラウザ
ed.notes=メモ
ed.group=グループ / フォルダー
ed.tags=タグ（カンマ区切り）
ed.status=ステータス
ed.create=プロファイルを作成
ed.save=変更を保存
ed.language=言語
ed.timezone=タイムゾーン
ed.screen=画面
ed.cores=CPU コア
ed.memory=メモリ
ed.gpu=WEBGL レンダラー
ed.regen=新しい指紋
ed.ext=組み込み拡張機能
ed.humanpaste=人間のように貼り付け
ed.proxywatch=プロキシ切り替え監視
ed.rightclick=右クリックを無効化
card.edit=プロファイルを編集
card.duplicate=複製
card.delete=プロファイルを削除
";

        private const string Ko = @"
nav.profiles=프로필
nav.settings=설정
nav.tools=도구
nav.about=정보
theme.night=야간 모드
theme.day=주간 모드
header.profiles=프로필
header.settings=설정
header.tools=도구 및 데이터
header.about=정보
btn.new=새 프로필
btn.batch=일괄 생성
btn.save=변경 내용 저장
btn.sort=정렬
btn.search=프로필 검색
btn.create=프로필 만들기
btn.openall=모두 열기
btn.stop=중지
btn.duplicate=복제
btn.delete=삭제
btn.cancel=취소
btn.open=열기
empty.title=프로필이 없습니다
empty.sub=첫 번째 ID를 만드세요. 각각 독립된 개인 브라우저입니다.
empty.nomatch=검색 결과 없음
empty.action=프로필 만들기
footer.local=로컬 우선 · 프라이빗
set.browser=브라우저 엔진
set.start=시작 페이지
set.storage=저장소
set.interface=인터페이스 언어
set.tools=프로필 도구
tab.general=일반
tab.fingerprint=지문
tab.privacy=개인정보
tab.proxy=프록시
tab.tools=도구
ed.new=새 프로필
ed.edit=프로필 편집
ed.name=프로필 이름
ed.start=시작 페이지
ed.browser=브라우저
ed.notes=메모
ed.group=그룹 / 폴더
ed.tags=태그 (쉼표로 구분)
ed.status=상태
ed.create=프로필 만들기
ed.save=변경 내용 저장
ed.language=언어
ed.timezone=시간대
ed.screen=화면
ed.cores=CPU 코어
ed.memory=메모리
ed.gpu=WEBGL 렌더러
ed.regen=새 지문
ed.ext=내장 확장 프로그램
ed.humanpaste=사람처럼 붙여넣기
ed.proxywatch=프록시 변경 감지
ed.rightclick=오른쪽 클릭 비활성화
card.edit=프로필 편집
card.duplicate=복제
card.delete=프로필 삭제
";
    }
}
