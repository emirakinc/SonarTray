"""Generate Strings.resx (English, neutral) and Strings.tr.resx from one table.

Keeping both languages in a single source means a key can never exist in one file
and be missing from the other.
"""
import io
import os
import xml.sax.saxutils as su

# key: (english, turkish, comment-for-translators)
S = {
    # --- channels -------------------------------------------------------
    "Channel_Master": ("Master", "Ana Ses", "Mixer channel name"),
    "Channel_Game": ("Game", "Oyun", "Mixer channel name"),
    "Channel_Chat": ("Chat", "Sohbet", "Mixer channel name"),
    "Channel_Media": ("Media", "Medya", "Mixer channel name"),
    "Channel_Mic": ("Microphone", "Mikrofon", "Mixer channel name"),
    "Channel_Aux": ("Aux", "Aux", "Mixer channel name"),

    # --- panel chrome ---------------------------------------------------
    "Panel_Title": ("Sonar Mixer", "Sonar Mikser", "Panel header"),
    "Panel_StartWithWindows": ("Start with Windows", "Windows ile başlat", "Checkbox"),
    "Panel_Exit": ("Exit", "Çıkış", "Button"),
    "Panel_Settings": ("Settings", "Ayarlar", "Title-bar button"),
    "Panel_OpenGg": ("Open SteelSeries GG", "SteelSeries GG'yi aç", "Title-bar button tooltip"),
    "Panel_Refresh": ("Refresh", "Yenile", "Title-bar button"),
    "Panel_MuteToggle": ("Mute / unmute", "Sustur / sesi aç", "Tooltip on the mute button"),
    "Panel_DeviceMissing": (
        "The selected device is disconnected, or the redirection is not running",
        "Seçili cihaz bağlı değil ya da yönlendirme çalışmıyor",
        "Warning icon tooltip on a channel row"),
    "Panel_Device": ("Output device", "Çıkış cihazı", "Accessibility name for the device picker"),
    "Panel_DeviceCapture": ("Input device", "Giriş cihazı", "Accessibility name for the mic device picker"),
    "Panel_Volume": ("Volume", "Ses", "Accessibility name for a volume slider"),

    # --- connection status ----------------------------------------------
    "Status_Searching": ("Looking for Sonar…", "Sonar aranıyor…", ""),
    "Status_Connected": ("Connected", "Bağlı", ""),
    "Status_StreamMode": ("Stream mode", "Stream modu", ""),
    "Status_NotFound": ("Sonar not found", "Sonar bulunamadı", ""),
    "Offline_Title": ("Sonar not found", "Sonar bulunamadı", "Overlay headline"),
    "Offline_Hint": ("Is SteelSeries GG running?", "SteelSeries GG çalışıyor mu?", "Overlay body"),
    "Offline_Retry": ("Try again", "Yeniden dene", "Overlay button"),

    # --- mode -----------------------------------------------------------
    "Mode_Classic": ("Classic", "Classic", "Sonar mode name; kept untranslated, GG shows it in English"),
    "Mode_Stream": ("Stream", "Stream", "Sonar mode name; kept untranslated, GG shows it in English"),
    "Mode_Switch": ("Switch mode", "Modu değiştir", "Tooltip"),
    "Mode_SwitchToClassic": ("Switch to Classic", "Classic moda geç", ""),
    "Mode_SwitchToStream": ("Switch to Stream", "Stream moda geç", ""),
    "Stream_Title": ("Sonar is in Stream mode", "Sonar Stream modunda", "Overlay headline"),
    "Stream_Hint": ("This tool only controls Classic mode.", "Bu araç yalnızca Classic modu destekler.", "Overlay body"),
    "Stream_Streaming": ("Stream", "Yayın", "Stream-mode sub-mix that goes out to viewers"),
    "Stream_Monitoring": ("Monitor", "Dinleme", "Stream-mode sub-mix the streamer hears"),

    # --- hotkeys page ---------------------------------------------------
    "Hotkeys_Title": ("Shortcuts", "Kısayollar", ""),
    "Hotkeys_Reset": ("Restore defaults", "Varsayılana dön", "Button"),
    "Hotkeys_Help": (
        "Click a shortcut and press the new combination. Esc cancels, Del clears. "
        "At least one modifier key is required.",
        "Bir kısayola tıklayıp yeni kombinasyona basın. Esc iptal eder, Del temizler. "
        "En az bir değiştirici tuş gerekir.",
        "Instructions above the shortcut list"),
    "Hotkeys_Clear": ("Remove", "Kaldır", "Accessibility name for the clear button"),
    "Hotkeys_ClearTip": ("Remove shortcut", "Kısayolu kaldır", "Tooltip"),
    "Hotkeys_PressKey": ("Press a key…", "Tuşa basın…", "Shown in the row being captured"),
    "Hotkeys_None": ("None", "Yok", "Shown when an action has no shortcut"),
    "Hotkeys_Taken": (
        "Another application already owns this combination",
        "Bu kombinasyonu başka bir uygulama kullanıyor",
        "Tooltip on a shortcut that failed to register"),

    # --- hotkey action names --------------------------------------------
    "Action_MicMute": ("Mute / unmute microphone", "Mikrofon sustur/aç", ""),
    "Action_MasterMute": ("Mute / unmute master", "Ana ses sustur/aç", ""),
    "Action_MasterUp": ("Master volume up", "Ana sesi artır", ""),
    "Action_MasterDown": ("Master volume down", "Ana sesi azalt", ""),
    "Action_TogglePanel": ("Show / hide panel", "Paneli aç/kapat", ""),
    "Action_GameUp": ("Game volume up", "Oyun sesini artır", ""),
    "Action_GameDown": ("Game volume down", "Oyun sesini azalt", ""),
    "Action_GameMute": ("Mute / unmute game", "Oyun sesini sustur/aç", ""),
    "Action_ChatUp": ("Chat volume up", "Sohbet sesini artır", ""),
    "Action_ChatDown": ("Chat volume down", "Sohbet sesini azalt", ""),
    "Action_ChatMute": ("Mute / unmute chat", "Sohbet sesini sustur/aç", ""),
    "Action_MediaUp": ("Media volume up", "Medya sesini artır", ""),
    "Action_MediaDown": ("Media volume down", "Medya sesini azalt", ""),
    "Action_MediaMute": ("Mute / unmute media", "Medya sesini sustur/aç", ""),

    # --- tray tooltips ---------------------------------------------------
    "Tray_Muted": ("SonarTray - master muted", "SonarTray – Ana ses kapalı", ""),
    "Tray_Volume": ("SonarTray - master {0}%", "SonarTray – Ana ses %{0}", "{0} is the volume percentage"),
    "Tray_StreamMode": ("SonarTray - Sonar is in Stream mode", "SonarTray – Sonar Stream modunda", ""),
    "Tray_Searching": ("SonarTray - looking for Sonar…", "SonarTray – Sonar aranıyor…", ""),
    "Tray_NotFound": ("SonarTray - Sonar not found", "SonarTray – Sonar bulunamadı", ""),

    # --- on-screen display ------------------------------------------------
    "Osd_Muted": ("Muted", "Kapalı", "Shown instead of a percentage when the channel is muted"),
    "Osd_Percent": ("{0}%", "%{0}", "{0} is the volume percentage; the sign goes first in Turkish"),

    # --- devices ----------------------------------------------------------
    "Device_NotConnected": ("(not connected)", "(bağlı değil)", "Placeholder for a device that is no longer present"),

    # --- audio profiles ----------------------------------------------------
    "Profiles_Title": ("Audio profile", "Ses profili", ""),
    "Profiles_Loading": ("Loading…", "Yükleniyor…", ""),
    "Profiles_None": ("No profiles", "Profil yok", ""),

    # --- mixer presets ------------------------------------------------------
    "Presets_Title": ("Presets", "Ön ayarlar", "Saved mixer snapshots"),
    "Presets_Save": ("Save", "Kaydet", "Button"),
    "Presets_NameHint": ("New preset name", "Yeni ön ayar adı", "Placeholder in the name box"),
    "Presets_None": ("No presets yet", "Henüz ön ayar yok", ""),
    "Presets_Delete": ("Delete", "Sil", "Accessibility name for the delete button"),
    "Presets_Full": ("Preset limit reached", "Ön ayar sınırına ulaşıldı", ""),

    # --- settings page ------------------------------------------------------
    "Settings_Title": ("Settings", "Ayarlar", ""),
    "Settings_Language": ("Language", "Dil", ""),
    "Settings_LanguageAuto": ("System language", "Sistem dili", "Language dropdown option"),
    "Settings_ShowAux": ("Show the Aux channel", "Aux kanalını göster", ""),
    "Settings_VolumeStep": ("Shortcut volume step", "Kısayol ses adımı", ""),
    "Settings_Osd": ("Show the on-screen display", "Ekran üstü göstergeyi göster", ""),
    "Settings_OsdDuration": ("Display duration", "Gösterim süresi", ""),
    "Settings_TrayWheel": ("Mouse wheel over the tray icon changes volume",
                           "Tepsi ikonu üzerinde fare tekerleği sesi değiştirsin", ""),
    "Settings_CheckUpdates": ("Check for updates", "Güncellemeleri denetle", ""),
    "Settings_RestartNeeded": ("Takes effect after a restart", "Yeniden başlatınca geçerli olur", ""),
    "Settings_Seconds": ("{0:0.0} s", "{0:0.0} sn", "{0} is a duration in seconds"),
    "Settings_OpenLog": ("Open the log folder", "Log klasörünü aç", "Button"),
    "Settings_Shortcuts": ("Keyboard shortcuts", "Klavye kısayolları", "Row that opens the hotkey page"),
    "Settings_Version": ("Version {0}", "Sürüm {0}", "{0} is a version like 0.2.0"),
    "Panel_Back": ("Back", "Geri", "Navigates one page back"),

    # --- updates --------------------------------------------------------------
    "Update_Available": ("Version {0} is available", "{0} sürümü yayımlandı", "{0} is a version like 0.2.0"),
    "Update_Download": ("Download", "İndir", "Button"),
    "Update_UpToDate": ("You are up to date", "Güncelsiniz", ""),
    "Update_Skip": ("Skip", "Atla", "Button: stop offering this particular version"),
}

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
'''


def write_resx(path, index, with_comments):
    parts = [HEADER]
    for key in S:
        value = su.escape(S[key][index])
        parts.append('  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n' % (key, value))
        comment = S[key][2]
        if with_comments and comment:
            parts.append('    <comment>%s</comment>\n' % su.escape(comment))
        parts.append('  </data>\n')
    parts.append('</root>\n')
    io.open(path, 'w', encoding='utf-8', newline='\n').write(''.join(parts))
    print('  %s  (%d anahtar)' % (path, len(S)))


def write_accessor(path):
    lines = [
        '// <auto-generated>',
        '//     Generated by tools/Generate-Strings.py from the table in that script.',
        '//     Do not edit by hand: add the key there and regenerate, so the English and',
        '//     Turkish tables cannot drift apart.',
        '// </auto-generated>',
        '',
        '// Auto-generated files opt out of the nullable context, so state it explicitly.',
        '#nullable enable',
        'using System.Globalization;',
        'using System.Resources;',
        '',
        'namespace SonarTray.Resources;',
        '',
        '/// <summary>',
        '/// Localised UI strings. English is the fallback for every language but Turkish.',
        '///',
        '/// Both languages are embedded in the main assembly rather than shipped as a culture',
        '/// satellite, because satellite assemblies are not bundled into a PublishSingleFile exe',
        '/// and the release is a single file.',
        '/// </summary>',
        'public static class Strings',
        '{',
        '    private static readonly ResourceManager English =',
        '        new("SonarTray.Resources.Strings", typeof(Strings).Assembly);',
        '',
        '    private static readonly ResourceManager Turkish =',
        '        new("SonarTray.Resources.Strings_tr", typeof(Strings).Assembly);',
        '',
        '    /// <summary>An explicit language choice; null follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>',
        '    public static CultureInfo? Culture { get; set; }',
        '',
        '    /// <summary>Every language code this build actually has strings for.</summary>',
        '    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { "en", "tr" };',
        '',
        '    /// <summary>Matches on the two-letter code, so tr-TR and tr-CY both get Turkish.</summary>',
        '    private static ResourceManager Active',
        '    {',
        '        get',
        '        {',
        '            var culture = Culture ?? CultureInfo.CurrentUICulture;',
        '            return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)',
        '                ? Turkish',
        '                : English;',
        '        }',
        '    }',
        '',
        '    /// <summary>',
        '    /// Falls back to English and then to the key itself, so a missing resource is visible',
        '    /// in the UI but never throws.',
        '    /// </summary>',
        '    public static string Get(string key)',
        '        => Active.GetString(key, CultureInfo.InvariantCulture)',
        '           ?? English.GetString(key, CultureInfo.InvariantCulture)',
        '           ?? key;',
        '',
        '    /// <summary>Formats a resource containing placeholders, in the active display culture.</summary>',
        '    public static string Format(string key, params object[] args)',
        '        => string.Format(Culture ?? CultureInfo.CurrentUICulture, Get(key), args);',
        '',
    ]
    for key in S:
        english = S[key][0].replace('\n', ' ')
        summary = english if len(english) <= 70 else english[:67] + '...'
        lines.append('    /// <summary>%s</summary>' % su.escape(summary))
        lines.append('    public static string %s => Get("%s");' % (key, key))
        lines.append('')
    lines.append('}')
    io.open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
    print('  %s' % path)


os.makedirs('Resources', exist_ok=True)
print('uretiliyor:')
write_resx(os.path.join('Resources', 'Strings.resx'), 0, True)
write_resx(os.path.join('Resources', 'Strings_tr.resx'), 1, False)
write_accessor(os.path.join('Resources', 'Strings.cs'))
