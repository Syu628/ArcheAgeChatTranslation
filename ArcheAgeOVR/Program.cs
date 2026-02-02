using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public class OverlayForm : Form
{
    private TabControl tabControl;
    private Dictionary<string, RichTextBox> chatBoxes = new Dictionary<string, RichTextBox>();
    private Point dragStart;
    private Button toggleEngineButton;
    private Button closeButton;
    private System.Windows.Forms.Timer monitorTimer;
    private int messageCount = 0;
    private int lastMessageCount = 0;
    private const int MaxLines = 1000;

    public OverlayForm()
    {
        InitializeUI();
        SubscribeToMessages();
        StartMonitorTimer();
        this.Shown += OverlayForm_Shown;

    }
    private void OverlayForm_Shown(object? sender, EventArgs e)
    {
        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat1",
            Text = "✅ OverlayForm 起動しました",
            Color = "aqua"
        });
        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat2",
            Text = "✅ OverlayForm 起動しました",
            Color = "aqua"
        });

        string engine = TranslatorWatcher.UseDeepL ? "DeepL" : "Google";
        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat1",
            Text = $"🌐 翻訳エンジン: {engine}",
            Color = "aqua"
        });
        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat2",
            Text = $"🌐 翻訳エンジン: {engine}",
            Color = "aqua"
        });

        string keyStatus = string.IsNullOrWhiteSpace(TranslatorWatcher.GetDeepLApiKey())
            ? "🔑 DeepL APIキー：未設定"
            : "🔑 DeepL APIキー：設定済み";

        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat1",
            Text = keyStatus,
            Color = string.IsNullOrWhiteSpace(TranslatorWatcher.GetDeepLApiKey()) ? "red" : "aqua"
        });
        ChatMessageBus.Send(new ChatMessage
        {
            Target = "chat2",
            Text = keyStatus,
            Color = string.IsNullOrWhiteSpace(TranslatorWatcher.GetDeepLApiKey()) ? "red" : "aqua"
        });
    }

    private void InitializeUI()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.TopMost = true;
        this.BackColor = Color.Black;
        this.Opacity = 0.8;
        this.Size = new Size(800, 600);
        this.Location = new Point(100, 100);

        this.MouseDown += GrabBar_MouseDown;
        this.MouseMove += GrabBar_MouseMove;

        tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed
        };
        tabControl.DrawItem += TabControl_DrawItem;
        this.Controls.Add(tabControl);

        AddChatTab("chat1");
        AddChatTab("chat2");

        closeButton = new Button
        {
            Text = "×",
            BackColor = Color.Gray,
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Width = 30,
            Height = 20,
            Top = 1,
            Left = this.Width - 40,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        closeButton.Click += (s, e) => this.Close();
        this.Controls.Add(closeButton);
        closeButton.BringToFront();

        toggleEngineButton = new Button
        {
            Text = "Google",
            Font = new Font("Meiryo UI", 8),
            BackColor = Color.LightGray,
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Width = 60,
            Height = 25,
            Top = 0,
            Left = this.Width - 110,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        toggleEngineButton.Click += (s, e) =>
        {
            TranslatorWatcher.UseDeepL = !TranslatorWatcher.UseDeepL;
            toggleEngineButton.Text = TranslatorWatcher.UseDeepL ? "DeepL" : "Google";
            //Debug.WriteLine($"[切替] 翻訳エンジン: {(TranslatorWatcher.UseDeepL ? "DeepL" : "Google")}");
            ChatMessageBus.Send(new ChatMessage
            {
                Target = "chat1",
                Text = $"[切替] 🔍翻訳エンジン: {(TranslatorWatcher.UseDeepL ? "DeepL" : "Google")}",
                Color = "aqua"
            });

        };
        this.Controls.Add(toggleEngineButton);
        toggleEngineButton.BringToFront();

    }


    private void AddChatTab(string key)
    {
        var tab = new TabPage($"チャット{key.Last()}");
        var rtb = CreateChatBox();
        tab.Controls.Add(rtb);
        tabControl.TabPages.Add(tab);
        chatBoxes[key] = rtb;
    }

    private void SubscribeToMessages()
    {
        ChatMessageBus.Subscribe((msg) =>
        {
            try
            {
                messageCount++;

                if (msg?.Target != null && chatBoxes.ContainsKey(msg.Target))
                {
                    this.Invoke((MethodInvoker)(() =>
                    {
                        try
                        {
                            AppendChat(chatBoxes[msg.Target], msg.Text, ParseColor(msg.Color));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[UI更新エラー]: {ex}");
                        }
                    }));
                }
                else
                {
                    Debug.WriteLine($"[未処理ターゲット]: {msg?.Target}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[メッセージ受信エラー]: {ex}");
            }
        });
    }

    private void AppendChat(RichTextBox rtb, string text, Color color)
    {
        rtb.SelectionColor = color;
        rtb.AppendText(text + Environment.NewLine);
        rtb.ScrollToCaret();

        if (rtb.Lines.Length > MaxLines)
        {
            int excess = rtb.Lines.Length - MaxLines;
            var lines = rtb.Lines;
            rtb.Lines = lines[excess..];
        }
    }

    private void StartMonitorTimer()
    {
        monitorTimer = new System.Windows.Forms.Timer
        {
            Interval = 30000
        };
        monitorTimer.Tick += (s, e) =>
        {
            try
            {
#if DEBUGMODE
                Debug.WriteLine($"[監視] 購読数: {ChatMessageBus.SubscriberCount}, メッセージ数: {messageCount}");
#endif
                if (messageCount == lastMessageCount)
                {
#if DEBUGMODE
                    Debug.WriteLine("[警告] メッセージ数が増えていません。購読切れまたは監視停止の可能性あり。");
#endif
                }

                lastMessageCount = messageCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[監視タイマーエラー]: {ex}");
            }
        };
        monitorTimer.Start();
    }

    private RichTextBox CreateChatBox()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Meiryo UI", 12, FontStyle.Bold),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
    }

    private void GrabBar_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            dragStart = new Point(e.X, e.Y);
        }
    }

    private void GrabBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            this.Left += e.X - dragStart.X;
            this.Top += e.Y - dragStart.Y;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            Point pos = PointToClient(new Point(m.LParam.ToInt32()));
            int grip = 10;

            if (pos.X < grip && pos.Y < grip) m.Result = (IntPtr)HTTOPLEFT;
            else if (pos.X > Width - grip && pos.Y < grip) m.Result = (IntPtr)HTTOPRIGHT;
            else if (pos.X < grip && pos.Y > Height - grip) m.Result = (IntPtr)HTBOTTOMLEFT;
            else if (pos.X > Width - grip && pos.Y > Height - grip) m.Result = (IntPtr)HTBOTTOMRIGHT;
            else if (pos.X < grip) m.Result = (IntPtr)HTLEFT;
            else if (pos.X > Width - grip) m.Result = (IntPtr)HTRIGHT;
            else if (pos.Y < grip) m.Result = (IntPtr)HTTOP;
            else if (pos.Y > Height - grip) m.Result = (IntPtr)HTBOTTOM;
            return;
        }

        base.WndProc(ref m);
    }

    private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
    {
        var page = tabControl.TabPages[e.Index];
        e.Graphics.FillRectangle(Brushes.Gray, e.Bounds);
        TextRenderer.DrawText(e.Graphics, page.Text, e.Font, e.Bounds, Color.White);
    }

    private Color ParseColor(string colorStr)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return Color.White;
            if (colorStr.StartsWith("#")) return ColorTranslator.FromHtml(colorStr);
            return Color.FromName(colorStr);
        }
        catch
        {
            return Color.White;
        }
    }




    [STAThread]
    public static void Main()
    {
        TranslatorWatcher.ReadMessageMain();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm());
    }
}
