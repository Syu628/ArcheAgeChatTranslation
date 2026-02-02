// #define DEBUGMODE
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Timers;
using System.Threading.Channels;

public class TranslatorWatcher
{
    //static string ADDON_BASE_DIR = @"../\";
    static string ADDON_BASE_DIR = @"C:\Users\admin\Documents\AAClassic\Addon";
    static string CHAT_INPUT_FILE = Path.Combine(ADDON_BASE_DIR, @"Japaneseplay\to_be_translated.lua");
    static Regex chatPattern = new Regex(@"chatMsg\s*=\s*""\[\[CHAT\|\|\|\|(.*?)\|\|\|\|(.*?)\|\|\|\|(.*?)\]\]""", RegexOptions.Singleline);
    static DateTime lastChatTrigger = DateTime.MinValue;
    static string lastTranslatedMessage = "";
    static FileSystemWatcher watcher;
    static System.Timers.Timer restartTimer;
    static System.Timers.Timer fileMonitorTimer;
    static DateTime lastFileWriteTime = DateTime.MinValue;

    public static bool UseDeepL { get; set; } = false;

    public static async Task<string> TranslateText(string text, string targetLang)
    {
        if (UseDeepL)
        {
            return await TranslateWithDeepL(text, targetLang);
        }
        else
        {
            return await TranslateWithGoogle(text, targetLang);
        }
    }

    private static async Task<string> TranslateWithGoogle(string text, string targetLang)
    {
        using var client = new HttpClient();
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
        try
        {
            var response = await client.GetStringAsync(url);
            var match = Regex.Match(response, @"\[\[\[\""(.*?)\""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Google翻訳エラー] {ex}");
            return null;
        }
    }


    private static readonly string DeepLEndpoint = "https://api-free.deepl.com/v2/translate";
    private static string DeepLApiKey = "";
    private static readonly string DeepLKeyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepl_api_key.txt");
    public static string GetDeepLApiKey()
    {
        return DeepLApiKey;
    }


    public static void LoadDeepLApiKey()
    {
        try
        {
            if (File.Exists(DeepLKeyFilePath))
            {
                DeepLApiKey = File.ReadAllText(DeepLKeyFilePath).Trim();
                //Debug.WriteLine("[DeepL APIキー読み込み成功]");
            }
            else
            {
                Debug.WriteLine("[DeepL APIキー未設定] ファイルが存在しません: " + DeepLKeyFilePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[DeepL APIキー読み込みエラー]: " + ex.Message);
        }
    }


    private static async Task<string> TranslateWithDeepL(string text, string targetLang)
    {
        try
        {
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("auth_key", DeepLApiKey),
            new KeyValuePair<string, string>("text", text),
            new KeyValuePair<string, string>("target_lang", targetLang.ToUpper())
        });

            var response = await client.PostAsync(DeepLEndpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[DeepL APIエラー] ステータス: {response.StatusCode}, 内容: {responseBody}");
                return null;
            }

            var match = Regex.Match(responseBody, @"""text"":\s*""(.*?)""");
            if (match.Success)
            {
                return System.Text.RegularExpressions.Regex.Unescape(match.Groups[1].Value);
            }

            Debug.WriteLine($"[DeepLレスポンス解析失敗] 内容: {responseBody}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeepL翻訳例外] {ex}");
            return null;
        }
    }



    public static void SendMessage(string target, string text, string color)
    {
        try
        {
            var message = new ChatMessage
            {
                Target = target,
                Text = text,
                Color = color
            };

            ChatMessageBus.Send(message);
#if DEBUGMODE
            Trace.WriteLine($"[送信] {target}: {text} ({color})");
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[送信エラー] {target}: {ex}");
        }
    }

    private static async Task<string> ReadFileWithRetry(string path, int maxRetries = 5, int delayMs = 100)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[ファイル読み込みリトライ {i + 1}/{maxRetries}] {ex.Message}");
                await Task.Delay(delayMs);
            }
        }

        throw new IOException($"ファイル {path} にアクセスできませんでした（{maxRetries} 回リトライ失敗）");
    }

    public static async Task ProcessChatFile()
    {
        try
        {
            string content = await ReadFileWithRetry(CHAT_INPUT_FILE);
            lastFileWriteTime = File.GetLastWriteTime(CHAT_INPUT_FILE);

            var match = chatPattern.Match(content);
            if (match.Success)
            {
                var channel = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                var message = match.Groups[3].Value;

                try
                {
                    await File.WriteAllTextAsync(CHAT_INPUT_FILE, "return {}", Encoding.UTF8);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"[ファイル初期化エラー]: {ex.Message}");
                }

                if (message == lastTranslatedMessage)
                {
#if DEBUGMODE
                    Trace.WriteLine("[スキップ] 同一メッセージ検出");
#endif
                    return;
                }
                lastTranslatedMessage = message;
#if DEBUGMODE
                Trace.WriteLine($"[検出] {name}({channel}): {message}");
#endif
                var translated = await TranslateText(message, "ja");
                if (translated != null)
                {
                    string chatText = $"{name}:{translated}";
                    SendMSG(chatText, channel);// オーバーレイに送信
                }
                else
                {
                    Debug.WriteLine("[翻訳失敗] null が返されました");
                }
            }
            else
            {
#if DEBUGMODE
                Debug.WriteLine("[無視] パターンに一致しませんでした");
#endif
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[チャット処理エラー]: {ex}");
        }
    }
    private static void SendMSG(string chatText, string channel)
    {
        string chatColor = "white";
        int sendChatTabState = 0;
        const int TAB1 = 0x01;
        const int TAB2 = 0x02;
        switch (channel)
        {
            case "0":   // 一般
                chatColor = "white";
                sendChatTabState |= TAB1;
                break;
            case "-3":  // ささやき(相手から)
            case "-4":  // ささやき(自分から)
                chatColor = "fuchsia";
                sendChatTabState |= TAB1;
                if (channel == "-4")
                {
                    chatText = "To " + chatText;
                }
                break;
            case "6":   // 勢力
                chatColor = "green";
                sendChatTabState |= TAB1;
                break;
            case "7":   // ギルド
                chatColor = "dodgerblue";
                sendChatTabState |= TAB1;
                break;

            case "11":  // 裁判
                chatColor = "orange";
                sendChatTabState |= TAB1;
                break;
            case "5":   // 攻撃隊
            case "10":  // 指揮官
                chatColor = "orange";
                sendChatTabState |= TAB1;
                sendChatTabState |= TAB2;
                if (channel == "10")
                {
                    chatText = "指揮官 " + chatText;
                }
                break;
            case "3":   // パーティ(自分から)
            case "4":   // パーティ(相手から)
                chatColor = "lightgreen";
                sendChatTabState |= TAB1;
                sendChatTabState |= TAB2;
                break;
            default:
                break;
        }

        if ((sendChatTabState & TAB1) == 1)
        {
            SendMessage("chat1", chatText, chatColor);
        }
        else if ((sendChatTabState & TAB2) == 1)
        {
            SendMessage("chat2", chatText, chatColor);
        }
        else
        {
#if DEBUGMODE
            Trace.WriteLine($"[未送信] 未対応チャネル: {channel}");
#endif
        }
    }

 //   public class TraceChatWriter : TextWriter
 //   {
 //       public override Encoding Encoding => Encoding.UTF8;
 //
 //       public override void WriteLine(string? value)
 //       {
 //           if (!string.IsNullOrWhiteSpace(value))
 //           {
 //               var msg1 = new ChatMessage { Target = "chat1", Text = value, Color = "red" };
 //               var msg2 = new ChatMessage { Target = "chat2", Text = value, Color = "red" };
 //               ChatMessageBus.Send(msg1);
 //               ChatMessageBus.Send(msg2);
 //           }
 //
 //           //System.Diagnostics.Trace.WriteLine(value); // 元のログも保持
 //       }
 //   }


    public static void ReadMessageMain()
    {
 //       Trace.Listeners.Clear();
 //       Trace.Listeners.Add(new TextWriterTraceListener(new TraceChatWriter()));


        LoadDeepLApiKey();
        try
        {
            string dir = Path.GetDirectoryName(CHAT_INPUT_FILE);
            if (!Directory.Exists(dir))
            {
                Debug.WriteLine("アドオンディレクトリが見つかりません。");
                return;
            }

            watcher = new FileSystemWatcher(dir)
            {
                Filter = Path.GetFileName(CHAT_INPUT_FILE),
                EnableRaisingEvents = true
            };

            watcher.Changed += async (_, __) =>
            {
                try
                {
                    var now = DateTime.Now;
                    if ((now - lastChatTrigger).TotalMilliseconds < 500) return;
                    lastChatTrigger = now;

                    await ProcessChatFile();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[監視イベントエラー]: {ex}");
                }
            };

            restartTimer = new System.Timers.Timer(60000);
            restartTimer.Elapsed += (_, __) =>
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.EnableRaisingEvents = true;
                    Debug.WriteLine("[監視再起動] FileSystemWatcher を再設定しました");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[監視再起動エラー]: {ex}");
                }
            };
            restartTimer.Start();

            fileMonitorTimer = new System.Timers.Timer(60000);
            fileMonitorTimer.Elapsed += (_, __) =>
            {
                try
                {
                    if (File.Exists(CHAT_INPUT_FILE))
                    {
                        var currentWriteTime = File.GetLastWriteTime(CHAT_INPUT_FILE);
                        if (currentWriteTime == lastFileWriteTime)
                        {
                            Debug.WriteLine("[警告] ファイル更新が止まっています。Lua 側の出力が停止している可能性あり。");
                        }
                        else
                        {
                            Debug.WriteLine($"[監視] ファイル更新日時: {currentWriteTime}");
                            lastFileWriteTime = currentWriteTime;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[警告] チャットファイルが存在しません。");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[更新監視エラー]: {ex}");
                }
            };
            fileMonitorTimer.Start();

            Debug.WriteLine("チャット監視を開始しました...");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[監視初期化エラー]: {ex}");
        }
    }
}
