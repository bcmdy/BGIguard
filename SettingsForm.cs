using System.Windows.Forms;

namespace BGIguard;

/// <summary>
/// 设置窗口
/// </summary>
public class SettingsForm : Form
{
    private TextBox _txtMemoryPercent = null!;
    private TextBox _txtMonitorInterval = null!;
    private CheckBox _chkShowOnStartup = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    // 配置路径
    private static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BGIguard_config.ini");

    public SettingsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "BGIguard 设置";
        this.Size = new System.Drawing.Size(400, 280);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        int labelWidth = 140;
        int controlLeft = 150;
        int rowHeight = 35;
        int startY = 20;

        // 内存阈值百分比
        var lblMemory = new Label
        {
            Text = "内存阈值 (%):",
            Location = new System.Drawing.Point(20, startY),
            Size = new System.Drawing.Size(labelWidth, 25)
        };
        this.Controls.Add(lblMemory);

        _txtMemoryPercent = new TextBox
        {
            Location = new System.Drawing.Point(controlLeft, startY),
            Size = new System.Drawing.Size(100, 25),
            Text = "80"
        };
        this.Controls.Add(_txtMemoryPercent);

        // 监控间隔
        var lblInterval = new Label
        {
            Text = "监控间隔 (秒):",
            Location = new System.Drawing.Point(20, startY + rowHeight),
            Size = new System.Drawing.Size(labelWidth, 25)
        };
        this.Controls.Add(lblInterval);

        _txtMonitorInterval = new TextBox
        {
            Location = new System.Drawing.Point(controlLeft, startY + rowHeight),
            Size = new System.Drawing.Size(100, 25),
            Text = "5"
        };
        this.Controls.Add(_txtMonitorInterval);

        // 启动时显示设置
        _chkShowOnStartup = new CheckBox
        {
            Text = "启动时显示设置窗口",
            Location = new System.Drawing.Point(20, startY + rowHeight * 2 + 10),
            Size = new System.Drawing.Size(200, 25),
            Checked = true
        };
        this.Controls.Add(_chkShowOnStartup);

        // 保存按钮
        _btnSave = new Button
        {
            Text = "保存",
            Location = new System.Drawing.Point(200, startY + rowHeight * 3 + 20),
            Size = new System.Drawing.Size(80, 30)
        };
        _btnSave.Click += BtnSave_Click;
        this.Controls.Add(_btnSave);

        // 取消按钮
        _btnCancel = new Button
        {
            Text = "取消",
            Location = new System.Drawing.Point(290, startY + rowHeight * 3 + 20),
            Size = new System.Drawing.Size(80, 30)
        };
        _btnCancel.Click += (s, e) => this.Close();
        this.Controls.Add(_btnCancel);

        // 说明标签
        var lblNote = new Label
        {
            Text = "说明: 内存阈值基于系统整体内存占用百分比",
            Location = new System.Drawing.Point(20, startY + rowHeight * 4 + 10),
            Size = new System.Drawing.Size(350, 25),
            ForeColor = System.Drawing.Color.Gray
        };
        this.Controls.Add(lblNote);
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var lines = File.ReadAllLines(ConfigFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    switch (parts[0])
                    {
                        case "MemoryPercent":
                            _txtMemoryPercent.Text = parts[1];
                            break;
                        case "MonitorInterval":
                            _txtMonitorInterval.Text = parts[1];
                            break;
                        case "ShowOnStartup":
                            _chkShowOnStartup.Checked = parts[1] == "1";
                            break;
                    }
                }
            }
        }
        catch
        {
            // 使用默认值
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            // 验证输入
            if (!int.TryParse(_txtMemoryPercent.Text, out int memoryPercent) || memoryPercent <= 0 || memoryPercent > 100)
            {
                MessageBox.Show("内存阈值应在 1-100 之间", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(_txtMonitorInterval.Text, out int interval) || interval <= 0)
            {
                MessageBox.Show("监控间隔应大于 0", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 保存配置
            var config = new Dictionary<string, string>
            {
                { "MemoryPercent", memoryPercent.ToString() },
                { "MonitorInterval", interval.ToString() },
                { "ShowOnStartup", _chkShowOnStartup.Checked ? "1" : "0" }
            };

            File.WriteAllLines(ConfigFilePath, config.Select(kv => $"{kv.Key}={kv.Value}"));

            MessageBox.Show("设置已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 加载配置
    /// </summary>
    public static (int memoryPercent, int monitorIntervalSeconds, bool showOnStartup) LoadConfig()
    {
        int memoryPercent = 80;
        int monitorIntervalSeconds = 5;
        bool showOnStartup = true;

        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var lines = File.ReadAllLines(ConfigFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    switch (parts[0])
                    {
                        case "MemoryPercent":
                            int.TryParse(parts[1], out memoryPercent);
                            break;
                        case "MonitorInterval":
                            int.TryParse(parts[1], out monitorIntervalSeconds);
                            break;
                        case "ShowOnStartup":
                            showOnStartup = parts[1] == "1";
                            break;
                    }
                }
            }
        }
        catch
        {
            // 使用默认值
        }

        return (memoryPercent, monitorIntervalSeconds, showOnStartup);
    }

    /// <summary>
    /// 清理配置文件（恢复默认）
    /// </summary>
    public static void ResetConfig()
    {
        if (File.Exists(ConfigFilePath))
        {
            File.Delete(ConfigFilePath);
        }
    }
}