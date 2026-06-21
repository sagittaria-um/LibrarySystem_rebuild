namespace LibrarySystem.Desktop;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        SuspendLayout();
        webView.AllowExternalDrop = false;
        webView.CreationProperties = null;
        webView.DefaultBackgroundColor = Color.White;
        webView.Dock = DockStyle.Fill;
        webView.Location = new Point(0, 0);
        webView.Name = "webView";
        webView.Size = new Size(1180, 760);
        webView.TabIndex = 0;
        webView.ZoomFactor = 1D;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 760);
        Controls.Add(webView);
        MinimumSize = new Size(980, 640);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "中山大学深圳校区图书管理系统";
        ((System.ComponentModel.ISupportInitialize)webView).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private Microsoft.Web.WebView2.WinForms.WebView2 webView;
}
