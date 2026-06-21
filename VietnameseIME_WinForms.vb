Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms
Imports Microsoft.Win32

' ============================================================
'  Bộ gõ Tiếng Việt Telex – WinForms
'  Hook toàn hệ thống (WH_KEYBOARD_LL) + SendInput (KEYEVENTF_UNICODE)
'  - Tray icon: V (đang bật) / E (đang tắt)
'  - Click đôi tray hoặc menu Toggle để bật/tắt
'  - Tuỳ chọn: khởi động cùng Windows, ẩn xuống tray
'  Version: 15062026
' ============================================================

#Region "WIN32 API"
Module Win32
    <DllImport("user32.dll", SetLastError:=True)>
    Public Function SendInput(nInputs As UInteger, pInputs() As INPUT, cbSize As Integer) As UInteger
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Public Structure INPUT
        Public type As UInteger
        Public U As InputUnion
    End Structure

    <StructLayout(LayoutKind.Explicit)>
    Public Structure InputUnion
        <FieldOffset(0)> Public ki As KEYBDINPUT
        <FieldOffset(0)> Public mi As MOUSEINPUT
        <FieldOffset(0)> Public hi As HARDWAREINPUT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure KEYBDINPUT
        Public wVk As UShort
        Public wScan As UShort
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure MOUSEINPUT
        Public dx As Integer
        Public dy As Integer
        Public mouseData As UInteger
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure HARDWAREINPUT
        Public uMsg As UInteger
        Public wParamL As UShort
        Public wParamH As UShort
    End Structure

    Public Const INPUT_KEYBOARD As UInteger = 1UI
    Public Const KEYEVENTF_KEYUP As UInteger = &H2UI
    Public Const KEYEVENTF_UNICODE As UInteger = &H4UI
    Public Const VK_BACK As UShort = &H8US

    ' ── API dùng cho Single-Instance: tìm & hiện lại cửa sổ đang chạy ──
    <DllImport("user32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Public Function FindWindow(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Public Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Public Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    Public Const SW_RESTORE As Integer = 9

    ' ── API dùng cho gửi byte TCVN3/VNI-Windows qua WM_CHAR ──
    Public Const WM_CHAR As UInteger = &H102UI

    <DllImport("user32.dll")>
    Public Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Function PostMessage(hWnd As IntPtr, Msg As UInteger,
                                wParam As UInteger, lParam As UInteger) As Boolean
    End Function
End Module
#End Region

' ════════════════════════════════════════════════════════════
'  Main Form
' ════════════════════════════════════════════════════════════
Public Class MainForm
    Inherits Form

#Region "Registry key"
    Private Const REG_RUN As String = "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    Private Const APP_NAME As String = "VietnameseIME"
    Private Const REG_SETTINGS As String = "SOFTWARE\VietnameseIME"
    Private Const VAL_INPUT_METHOD As String = "InputMethod"
    Private Const VAL_GAME_MODE As String = "GameMode"
    Private Const VAL_OUTPUT_ENCODING As String = "OutputEncoding"
#End Region

#Region "Input Method"
    Public Enum InputMethodType
        Telex
        VNI
    End Enum
#End Region

#Region "Output Encoding"
    ''' <summary>
    ''' Bảng mã đầu ra khi gửi ký tự vào ứng dụng đích.
    ''' Unicode  : UTF-16 (mặc định, dùng KEYEVENTF_UNICODE)
    ''' TCVN3    : Bảng mã TCVN 5712-1:1993 (2 trang ABC / ABC+)
    ''' VNIWindows: Bảng mã VNI Windows (Windows-1258 / CP1258)
    ''' </summary>
    Public Enum OutputEncodingType
        Unicode
        TCVN3
        VNIWindows
    End Enum
#End Region

#Region "Controls"
    Private chkStartup As CheckBox
    Private chkMinimizeToTray As CheckBox
    Private chkGameMode As CheckBox
    Private lblGameStatus As Label
    Private lblStatus As Label
    Private btnToggle As Button
    Private lblMethod As Label
    Private rdoTelex As RadioButton
    Private rdoVNI As RadioButton
    Private lblEncoding As Label
    Private cboEncoding As ComboBox
    Private trayIcon As NotifyIcon
    Private trayMenu As ContextMenuStrip
    Private trayToggleItem As ToolStripMenuItem
    Private trayTelexItem As ToolStripMenuItem
    Private trayVniItem As ToolStripMenuItem
#End Region

#Region "IME State"
    Private _hook As GlobalHook
    Private _trayClickTimer As Timer
    Private _enabled As Boolean = True   ' bật/tắt tiếng Việt
    Private _inputMethod As InputMethodType = InputMethodType.Telex
    Private _outputEncoding As OutputEncodingType = OutputEncodingType.Unicode
    Private _buf As New StringBuilder()
    Private _lastToneKey As Char = Chr(0)

    ' ── Chế độ Game ──
    ' Khi bật: phím dấu/chữ chỉ được IME xử lý lúc khung chat đang "mở" (đã
    ' bấm Enter); còn lại nhả nguyên cho game dùng làm hotkey (R, F, S, X...).
    Private _gameModeEnabled As Boolean = False
    Private _chatOpenViaEnter As Boolean = False
#End Region

#Region "Telex tables"
    Private ReadOnly VowelModifiers As New Dictionary(Of String, Char)()
    Private ReadOnly ToneKeys As New HashSet(Of Char)({"s"c, "f"c, "r"c, "x"c, "j"c, "z"c})
    Private ReadOnly ToneTable As New Dictionary(Of Char, Dictionary(Of Char, Char))()
    Private ReadOnly TonedToBase As New Dictionary(Of Char, Char)()
    Private ReadOnly BaseVowels As New HashSet(Of Char)("aăâeêiouưôơy".ToCharArray())
    Private ReadOnly VowelGroupMainPos As New Dictionary(Of String, Integer)()
#End Region

#Region "VNI tables"
    ' Cặp [nguyên âm/phụ âm]+[số] → ký tự biến hình (â,ă,ê,ô,ơ,ư,đ). VD: "a6"→â, "d9"→đ
    Private ReadOnly VniVowelModifiers As New Dictionary(Of String, Char)()
    ' Số dấu thanh VNI (1 sắc,2 huyền,3 hỏi,4 ngã,5 nặng,0 xoá dấu) → mã dấu nội bộ
    ' dùng chung với Telex (s,f,r,x,j,z) để tái sử dụng ToneTable/ApplyTone.
    Private ReadOnly VniToneDigits As New Dictionary(Of Char, Char)()
#End Region

#Region "Output encoding tables"
    ' Unicode → TCVN3 (byte đơn, trang ABC font TCVN3 chuẩn)
    ' Mỗi ký tự Unicode ánh xạ sang 1 byte TCVN3 (gửi qua SendVirtualKey/WM_CHAR).
    ' TCVN3 dùng 2 font: ABC (0x20-0xFE) cho chữ thường và ABC+ cho chữ hoa có dấu.
    ' Ta ghép 2 trang vào 1 dict: value là String thay vì Char vì một số ký tự Unicode
    ' cần 2 byte TCVN3 (ký tự hoa có dấu nằm ở trang ABC+, nhưng ở đây ta đơn giản
    ' dùng 1 byte duy nhất từ bảng ghép bên dưới — đủ cho 99% trường hợp thực tế).
    ' Shared: dùng chung cho cả tính năng gõ trực tiếp (SendTCVN3String) và
    ' tính năng "Chuyển đổi mã" (FrmConvert) — không cần tạo MainForm mới.
    Private Shared ReadOnly TCVN3Map As New Dictionary(Of Char, Byte)()
    ' Bảng đảo ngược byte TCVN3 → Unicode, dựng tự động từ TCVN3Map (xem InitEncodingMaps).
    Private Shared ReadOnly TCVN3ReverseMap As New Dictionary(Of Byte, Char)()
    ' Unicode → VNI-Windows (Windows-1258 / CP1258)
    Private Shared ReadOnly VNIWinMap As New Dictionary(Of Char, Byte)()
#End Region

    ' ── Constructor ──────────────────────────────────────────
    Public Sub New()
        InitTables()
        BuildUI()
        BuildTrayIcon()
        LoadSettings()

        ' Bật cả hook chuột (True) lẫn hook bàn phím (True): cần biết khi nào
        ' người dùng click/chọn vùng bằng chuột để xoá buffer kịp lúc — nếu
        ' không, buffer cũ vẫn còn khi con trỏ đã dời chỗ bằng chuột, dẫn đến
        ' áp dấu / xoá lùi nhầm vào vị trí mới.
        _hook = New GlobalHook(True, True)
        AddHandler _hook.KeyDown, AddressOf HandleKeyDown
        AddHandler _hook.KeyPress, AddressOf HandleKeyPress
        AddHandler _hook.OnMouseActivity, AddressOf HandleMouseActivity

        UpdateTrayIcon()
    End Sub

#Region "UI Builder"
    Private Sub BuildUI()
        Me.Text = "Bộ gõ Tiếng Việt Telex"
        Me.Size = New Size(320, 335)
        Me.FormBorderStyle = FormBorderStyle.FixedSingle
        Me.MaximizeBox = False
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.BackColor = Color.FromArgb(245, 245, 245)

        ' ── Label trạng thái ──
        lblStatus = New Label()
        lblStatus.Text = "● Đang bật"
        lblStatus.ForeColor = Color.FromArgb(0, 153, 76)
        lblStatus.Font = New Font("Segoe UI", 11, FontStyle.Bold)
        lblStatus.Location = New Point(20, 18)
        lblStatus.AutoSize = True
        Me.Controls.Add(lblStatus)

        ' ── Nút bật/tắt ──
        btnToggle = New Button()
        btnToggle.Text = "Tắt tiếng Việt"
        btnToggle.Size = New Size(130, 32)
        btnToggle.Location = New Point(160, 12)
        btnToggle.FlatStyle = FlatStyle.Flat
        btnToggle.BackColor = Color.FromArgb(220, 53, 69)
        btnToggle.ForeColor = Color.White
        btnToggle.Font = New Font("Segoe UI", 9, FontStyle.Bold)
        btnToggle.FlatAppearance.BorderSize = 0
        AddHandler btnToggle.Click, AddressOf OnToggleClick
        Me.Controls.Add(btnToggle)

        ' ── Kiểu gõ: Telex / VNI ──
        lblMethod = New Label()
        lblMethod.Text = "Kiểu gõ:"
        lblMethod.Font = New Font("Segoe UI", 9)
        lblMethod.Location = New Point(20, 73)
        lblMethod.AutoSize = True
        Me.Controls.Add(lblMethod)

        rdoTelex = New RadioButton()
        rdoTelex.Text = "Telex"
        rdoTelex.Font = New Font("Segoe UI", 9)
        rdoTelex.Location = New Point(90, 71)
        rdoTelex.AutoSize = True
        rdoTelex.Checked = True
        AddHandler rdoTelex.CheckedChanged, AddressOf OnInputMethodChanged
        Me.Controls.Add(rdoTelex)

        rdoVNI = New RadioButton()
        rdoVNI.Text = "VNI"
        rdoVNI.Font = New Font("Segoe UI", 9)
        rdoVNI.Location = New Point(175, 71)
        rdoVNI.AutoSize = True
        AddHandler rdoVNI.CheckedChanged, AddressOf OnInputMethodChanged
        Me.Controls.Add(rdoVNI)

        ' ── Bảng mã đầu ra ──
        lblEncoding = New Label()
        lblEncoding.Text = "Bảng mã ra:"
        lblEncoding.Font = New Font("Segoe UI", 9)
        lblEncoding.Location = New Point(20, 103)
        lblEncoding.AutoSize = True
        Me.Controls.Add(lblEncoding)

        cboEncoding = New ComboBox()
        cboEncoding.Font = New Font("Segoe UI", 9)
        cboEncoding.Location = New Point(110, 100)
        cboEncoding.Size = New Size(185, 24)
        cboEncoding.DropDownStyle = ComboBoxStyle.DropDownList
        cboEncoding.Items.Add("Unicode (UTF-8)")
        cboEncoding.Items.Add("TCVN3 (ABC/ABC+)")
        cboEncoding.Items.Add("VNI Windows (CP1258)")
        cboEncoding.SelectedIndex = 0
        AddHandler cboEncoding.SelectedIndexChanged, AddressOf OnEncodingChanged
        Me.Controls.Add(cboEncoding)

        ' ── Separator ──
        Dim sep2 As New Label()
        sep2.BorderStyle = BorderStyle.Fixed3D
        sep2.Size = New Size(278, 2)
        sep2.Location = New Point(20, 132)
        Me.Controls.Add(sep2)

        ' ── Checkbox khởi động cùng hệ thống ──
        chkStartup = New CheckBox()
        chkStartup.Text = "Khởi động cùng Windows"
        chkStartup.Font = New Font("Segoe UI", 9)
        chkStartup.Location = New Point(20, 141)
        chkStartup.AutoSize = True
        AddHandler chkStartup.CheckedChanged, AddressOf OnStartupChanged
        Me.Controls.Add(chkStartup)

        ' ── Checkbox ẩn xuống tray ──
        chkMinimizeToTray = New CheckBox()
        chkMinimizeToTray.Text = "Ẩn xuống tray khi đóng cửa sổ"
        chkMinimizeToTray.Font = New Font("Segoe UI", 9)
        chkMinimizeToTray.Location = New Point(20, 165)
        chkMinimizeToTray.AutoSize = True
        AddHandler chkMinimizeToTray.CheckedChanged, AddressOf OnMinimizeToTrayChanged
        Me.Controls.Add(chkMinimizeToTray)

        ' ── Checkbox Chế độ Game ──
        chkGameMode = New CheckBox()
        chkGameMode.Text = "Chế độ Game (Enter để mở/đóng khung chat)"
        chkGameMode.Font = New Font("Segoe UI", 9)
        chkGameMode.Location = New Point(20, 189)
        chkGameMode.AutoSize = True
        AddHandler chkGameMode.CheckedChanged, AddressOf OnGameModeChanged
        Me.Controls.Add(chkGameMode)

        lblGameStatus = New Label()
        lblGameStatus.Text = ""
        lblGameStatus.Font = New Font("Segoe UI", 8, FontStyle.Italic)
        lblGameStatus.ForeColor = Color.Gray
        lblGameStatus.Location = New Point(38, 211)
        lblGameStatus.AutoSize = True
        lblGameStatus.Visible = False
        Me.Controls.Add(lblGameStatus)

        ' ── Nút mở cửa sổ Chuyển đổi mã ──
        Dim btnConvert As New Button()
        btnConvert.Text = "Chuyển đổi mã..."
        btnConvert.Size = New Size(278, 30)
        btnConvert.Location = New Point(20, 238)
        btnConvert.FlatStyle = FlatStyle.Flat
        btnConvert.BackColor = Color.FromArgb(0, 120, 215)
        btnConvert.ForeColor = Color.White
        btnConvert.Font = New Font("Segoe UI", 9, FontStyle.Bold)
        btnConvert.FlatAppearance.BorderSize = 0
        AddHandler btnConvert.Click, AddressOf OnOpenConverterClick
        Me.Controls.Add(btnConvert)

        ' ── Version label ──
        Dim lblVer As New Label()
        lblVer.Text = "v21062026  –  2CongLC"
        lblVer.ForeColor = Color.Gray
        lblVer.Font = New Font("Segoe UI", 7.5)
        lblVer.Location = New Point(20, 278)
        lblVer.AutoSize = True
        Me.Controls.Add(lblVer)

        ' ── Form events ──
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
        AddHandler Me.Resize, AddressOf OnFormResize
    End Sub

    Private Sub BuildTrayIcon()
        trayMenu = New ContextMenuStrip()

        trayToggleItem = New ToolStripMenuItem("Tắt tiếng Việt")
        AddHandler trayToggleItem.Click, AddressOf OnToggleClick
        trayMenu.Items.Add(trayToggleItem)

        trayMenu.Items.Add(New ToolStripSeparator())

        Dim methodMenu As New ToolStripMenuItem("Kiểu gõ")
        trayTelexItem = New ToolStripMenuItem("Telex")
        AddHandler trayTelexItem.Click, AddressOf OnTrayTelexClick
        methodMenu.DropDownItems.Add(trayTelexItem)

        trayVniItem = New ToolStripMenuItem("VNI")
        AddHandler trayVniItem.Click, AddressOf OnTrayVniClick
        methodMenu.DropDownItems.Add(trayVniItem)

        trayMenu.Items.Add(methodMenu)

        trayMenu.Items.Add(New ToolStripSeparator())

        ' ── Menu Chuyển đổi mã ──
        Dim convertMenu As New ToolStripMenuItem("Chuyển đổi mã")

        Dim openConvertItem As New ToolStripMenuItem("Mở cửa sổ chuyển đổi...")
        AddHandler openConvertItem.Click, AddressOf OnOpenConverterClick
        convertMenu.DropDownItems.Add(openConvertItem)

        convertMenu.DropDownItems.Add(New ToolStripSeparator())

        Dim quickLabels As String() = {
            "Clipboard: Unicode → TCVN3",
            "Clipboard: TCVN3 → Unicode",
            "Clipboard: Unicode → VNI Windows",
            "Clipboard: VNI Windows → Unicode"
        }
        Dim quickFrom As OutputEncodingType() = {
            OutputEncodingType.Unicode, OutputEncodingType.TCVN3,
            OutputEncodingType.Unicode, OutputEncodingType.VNIWindows
        }
        Dim quickTo As OutputEncodingType() = {
            OutputEncodingType.TCVN3, OutputEncodingType.Unicode,
            OutputEncodingType.VNIWindows, OutputEncodingType.Unicode
        }
        For i As Integer = 0 To quickLabels.Length - 1
            Dim item As New ToolStripMenuItem(quickLabels(i))
            item.Tag = New OutputEncodingType() {quickFrom(i), quickTo(i)}
            AddHandler item.Click, AddressOf OnTrayQuickConvertClick
            convertMenu.DropDownItems.Add(item)
        Next

        trayMenu.Items.Add(convertMenu)

        trayMenu.Items.Add(New ToolStripSeparator())

        Dim showItem As New ToolStripMenuItem("Mở cửa sổ")
        AddHandler showItem.Click, AddressOf OnTrayDoubleClick
        trayMenu.Items.Add(showItem)

        trayMenu.Items.Add(New ToolStripSeparator())

        Dim exitItem As New ToolStripMenuItem("Thoát")
        AddHandler exitItem.Click, AddressOf OnTrayExitClick
        trayMenu.Items.Add(exitItem)

        trayIcon = New NotifyIcon()
        trayIcon.ContextMenuStrip = trayMenu
        trayIcon.Visible = True
        trayIcon.Text = "Bộ gõ Tiếng Việt – Đang bật"
        AddHandler trayIcon.MouseUp, AddressOf OnTrayMouseUp
        AddHandler trayIcon.DoubleClick, AddressOf OnTrayDoubleClick
    End Sub
#End Region

#Region "Tray Icon Drawing"
    ''' <summary>
    ''' Vẽ icon chữ "V" (xanh lá – bật) hoặc "E" (xám – tắt) 16x16
    ''' </summary>
    Private Sub UpdateTrayIcon()
        Dim bmp As New Bitmap(16, 16)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.Clear(Color.Transparent)
            If _enabled Then
                ' Nền xanh lá
                g.FillRectangle(New SolidBrush(Color.FromArgb(0, 153, 76)), 0, 0, 16, 16)
                ' Chữ V trắng
                Dim sf As New StringFormat()
                sf.Alignment = StringAlignment.Center
                sf.LineAlignment = StringAlignment.Center
                g.DrawString("V", New Font("Arial", 9, FontStyle.Bold),
                             Brushes.White, New RectangleF(0, 0, 16, 16), sf)
            Else
                ' Nền xám
                g.FillRectangle(New SolidBrush(Color.FromArgb(120, 120, 120)), 0, 0, 16, 16)
                ' Chữ E trắng
                Dim sf As New StringFormat()
                sf.Alignment = StringAlignment.Center
                sf.LineAlignment = StringAlignment.Center
                g.DrawString("E", New Font("Arial", 9, FontStyle.Bold),
                             Brushes.White, New RectangleF(0, 0, 16, 16), sf)
            End If
        End Using
        Dim hIcon As IntPtr = bmp.GetHicon()
        trayIcon.Icon = Icon.FromHandle(hIcon)
        trayIcon.Text = If(_enabled, "Bộ gõ Tiếng Việt – Đang bật", "Bộ gõ Tiếng Việt – Đang tắt")

        ' Cập nhật UI
        If _enabled Then
            lblStatus.Text = "● Đang bật"
            lblStatus.ForeColor = Color.FromArgb(0, 153, 76)
            btnToggle.Text = "Tắt tiếng Việt"
            btnToggle.BackColor = Color.FromArgb(220, 53, 69)
            trayToggleItem.Text = "Tắt tiếng Việt"
        Else
            lblStatus.Text = "○ Đã tắt"
            lblStatus.ForeColor = Color.FromArgb(150, 150, 150)
            btnToggle.Text = "Bật tiếng Việt"
            btnToggle.BackColor = Color.FromArgb(0, 153, 76)
            trayToggleItem.Text = "Bật tiếng Việt"
        End If
    End Sub
#End Region

#Region "Toggle / Settings"
    Private Sub OnToggleClick(sender As Object, e As EventArgs)
        _enabled = Not _enabled
        _buf.Clear()
        _lastToneKey = Chr(0)
        _chatOpenViaEnter = False
        UpdateGameStatusLabel()
        UpdateTrayIcon()
    End Sub

    Private Sub OnStartupChanged(sender As Object, e As EventArgs)
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_RUN, True)
            If chkStartup.Checked Then
                key.SetValue(APP_NAME, """" & Application.ExecutablePath & """")
            Else
                key.DeleteValue(APP_NAME, False)
            End If
            key.Close()
        Catch ex As Exception
            MessageBox.Show("Không thể ghi registry: " & ex.Message,
                            "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OnMinimizeToTrayChanged(sender As Object, e As EventArgs)
        ' giá trị được đọc trực tiếp từ checkbox khi cần
    End Sub

    Private Sub OnGameModeChanged(sender As Object, e As EventArgs)
        _gameModeEnabled = chkGameMode.Checked
        _chatOpenViaEnter = False
        _buf.Clear()
        _lastToneKey = Chr(0)
        UpdateGameStatusLabel()
        SaveGameModeSetting(_gameModeEnabled)
    End Sub

    Private Sub SaveGameModeSetting(enabled As Boolean)
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_SETTINGS)
            key.SetValue(VAL_GAME_MODE, If(enabled, "1", "0"))
            key.Close()
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Cập nhật dòng trạng thái nhỏ dưới checkbox Chế độ Game, cho biết hiện
    ''' tại phím dấu đang được IME xử lý hay đang nhả nguyên cho game.
    ''' </summary>
    Private Sub UpdateGameStatusLabel()
        If Not _gameModeEnabled Then
            lblGameStatus.Visible = False
            Return
        End If
        lblGameStatus.Visible = True
        If _chatOpenViaEnter Then
            lblGameStatus.Text = "→ Khung chat ĐANG MỞ: gõ tiếng Việt"
            lblGameStatus.ForeColor = Color.FromArgb(0, 153, 76)
        Else
            lblGameStatus.Text = "→ Khung chat đang đóng: nhả phím cho game"
            lblGameStatus.ForeColor = Color.Gray
        End If
    End Sub

    ''' <summary>
    ''' IME có nên xử lý/chặn phím vừa gõ không. Ngoài việc bật/tắt tổng
    ''' (_enabled), khi Chế độ Game đang bật thì còn phải đang ở trong khung
    ''' chat (_chatOpenViaEnter) mới xử lý — nếu không thì nhả nguyên cho game.
    ''' </summary>
    Private Function IsImeActive() As Boolean
        Return _enabled AndAlso (Not _gameModeEnabled OrElse _chatOpenViaEnter)
    End Function

    ''' <summary>
    ''' Người dùng đổi radio Telex/VNI trên cửa sổ chính.
    ''' </summary>
    Private Sub OnInputMethodChanged(sender As Object, e As EventArgs)
        If rdoVNI.Checked Then
            SetInputMethod(InputMethodType.VNI)
        Else
            SetInputMethod(InputMethodType.Telex)
        End If
    End Sub

    Private Sub OnEncodingChanged(sender As Object, e As EventArgs)
        Dim enc As OutputEncodingType = CType(cboEncoding.SelectedIndex, OutputEncodingType)
        SetOutputEncoding(enc)
    End Sub

    Private Sub SetOutputEncoding(enc As OutputEncodingType, Optional saveSetting As Boolean = True)
        _outputEncoding = enc
        _buf.Clear()
        _lastToneKey = Chr(0)
        cboEncoding.SelectedIndex = CInt(enc)
        If saveSetting Then SaveOutputEncodingSetting(enc)
    End Sub

    Private Sub SaveOutputEncodingSetting(enc As OutputEncodingType)
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_SETTINGS)
            key.SetValue(VAL_OUTPUT_ENCODING, enc.ToString())
            key.Close()
        Catch
        End Try
    End Sub

    Private Sub OnTrayTelexClick(sender As Object, e As EventArgs)
        SetInputMethod(InputMethodType.Telex)
    End Sub

    Private Sub OnTrayVniClick(sender As Object, e As EventArgs)
        SetInputMethod(InputMethodType.VNI)
    End Sub

    ''' <summary>Mở cửa sổ "Chuyển đổi mã" (dùng chung cho nút trên form chính và menu tray).</summary>
    Private Sub OnOpenConverterClick(sender As Object, e As EventArgs)
        Dim frm As New FrmConvert()
        frm.Show()
        frm.BringToFront()
    End Sub

    ''' <summary>
    ''' Chuyển đổi nhanh nội dung Clipboard theo cặp mã (from, to) lưu trong Tag
    ''' của ToolStripMenuItem tương ứng, rồi ghi kết quả ngược lại Clipboard.
    ''' </summary>
    Private Sub OnTrayQuickConvertClick(sender As Object, e As EventArgs)
        Dim item As ToolStripMenuItem = TryCast(sender, ToolStripMenuItem)
        If item Is Nothing Then Return
        Dim pair As OutputEncodingType() = TryCast(item.Tag, OutputEncodingType())
        If pair Is Nothing OrElse pair.Length <> 2 Then Return

        Try
            If Not Clipboard.ContainsText() Then
                trayIcon.ShowBalloonTip(2000, "Chuyển đổi mã", "Clipboard không có văn bản.", ToolTipIcon.Warning)
                Return
            End If
            Dim original As String = Clipboard.GetText()
            Dim result As String = ConvertVietnameseText(original, pair(0), pair(1))
            Clipboard.SetText(result)
            trayIcon.ShowBalloonTip(2000, "Chuyển đổi mã",
                "Đã chuyển đổi và dán kết quả vào Clipboard (Ctrl+V để dùng).", ToolTipIcon.Info)
        Catch ex As Exception
            trayIcon.ShowBalloonTip(2000, "Chuyển đổi mã", "Lỗi: " & ex.Message, ToolTipIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' Đổi kiểu gõ hiện tại: đồng bộ radio + tray menu, xoá buffer đang gõ,
    ''' và (tuỳ chọn) lưu lựa chọn vào registry để nhớ cho lần mở sau.
    ''' </summary>
    Private Sub SetInputMethod(method As InputMethodType, Optional saveSetting As Boolean = True)
        _inputMethod = method
        _buf.Clear()
        _lastToneKey = Chr(0)

        rdoTelex.Checked = (method = InputMethodType.Telex)
        rdoVNI.Checked = (method = InputMethodType.VNI)
        trayTelexItem.Checked = (method = InputMethodType.Telex)
        trayVniItem.Checked = (method = InputMethodType.VNI)

        If saveSetting Then SaveInputMethodSetting(method)
    End Sub

    Private Sub SaveInputMethodSetting(method As InputMethodType)
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_SETTINGS)
            key.SetValue(VAL_INPUT_METHOD, method.ToString())
            key.Close()
        Catch
        End Try
    End Sub

    Private Sub LoadSettings()
        ' Đọc trạng thái startup từ registry
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_RUN, False)
            If key IsNot Nothing Then
                chkStartup.Checked = (key.GetValue(APP_NAME) IsNot Nothing)
                key.Close()
            End If
        Catch
        End Try
        ' Mặc định ẩn tray = bật
        chkMinimizeToTray.Checked = True

        ' Đọc trạng thái Chế độ Game đã lưu, mặc định tắt nếu chưa có
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_SETTINGS, False)
            If key IsNot Nothing Then
                Dim gm As String = TryCast(key.GetValue(VAL_GAME_MODE), String)
                chkGameMode.Checked = (gm = "1")
                key.Close()
            End If
        Catch
        End Try
        _gameModeEnabled = chkGameMode.Checked
        _chatOpenViaEnter = False
        UpdateGameStatusLabel()

        ' Đọc kiểu gõ đã lưu (Telex/VNI), mặc định Telex nếu chưa có
        Dim savedMethod As InputMethodType = InputMethodType.Telex
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_SETTINGS, False)
            If key IsNot Nothing Then
                Dim v As String = TryCast(key.GetValue(VAL_INPUT_METHOD), String)
                If String.Equals(v, InputMethodType.VNI.ToString(), StringComparison.OrdinalIgnoreCase) Then
                    savedMethod = InputMethodType.VNI
                End If
                key.Close()
            End If
        Catch
        End Try
        SetInputMethod(savedMethod, False)

        ' Đọc bảng mã đầu ra đã lưu (Unicode/TCVN3/VNIWindows)
        Try
            Dim key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_SETTINGS, False)
            If key IsNot Nothing Then
                Dim v As String = TryCast(key.GetValue(VAL_OUTPUT_ENCODING), String)
                Dim enc As OutputEncodingType = OutputEncodingType.Unicode
                If String.Equals(v, OutputEncodingType.TCVN3.ToString(), StringComparison.OrdinalIgnoreCase) Then
                    enc = OutputEncodingType.TCVN3
                ElseIf String.Equals(v, OutputEncodingType.VNIWindows.ToString(), StringComparison.OrdinalIgnoreCase) Then
                    enc = OutputEncodingType.VNIWindows
                End If
                key.Close()
                SetOutputEncoding(enc, False)
            End If
        Catch
        End Try
    End Sub
#End Region

#Region "Form / Tray Events"
    Private Sub HandleFormClosing(sender As Object, e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing AndAlso chkMinimizeToTray.Checked Then
            e.Cancel = True
            Me.Hide()
        Else
            _hook.Stop()
            trayIcon.Visible = False
        End If
    End Sub

    Private Sub OnFormResize(sender As Object, e As EventArgs)
        If Me.WindowState = FormWindowState.Minimized AndAlso chkMinimizeToTray.Checked Then
            Me.Hide()
        End If
    End Sub

    ''' <summary>
    ''' MouseUp thay vì Click để phân biệt được single/double.
    ''' Timer 300ms: nếu không có DoubleClick theo sau → toggle.
    ''' </summary>
    Private Sub OnTrayMouseUp(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Return
        If _trayClickTimer Is Nothing Then
            _trayClickTimer = New Timer()
            _trayClickTimer.Interval = SystemInformation.DoubleClickTime + 50
            AddHandler _trayClickTimer.Tick, AddressOf OnTrayClickTimerTick
        End If
        _trayClickTimer.Stop()
        _trayClickTimer.Start()
    End Sub

    Private Sub OnTrayClickTimerTick(sender As Object, e As EventArgs)
        _trayClickTimer.Stop()
        ' Hết thời gian chờ mà không có DoubleClick → toggle
        OnToggleClick(sender, e)
    End Sub

    Private Sub OnTrayDoubleClick(sender As Object, e As EventArgs)
        ' Huỷ timer để không toggle
        If _trayClickTimer IsNot Nothing Then _trayClickTimer.Stop()
        ' Mở cửa sổ
        Me.Show()
        Me.WindowState = FormWindowState.Normal
        Me.BringToFront()
    End Sub

    Private Sub OnTrayExitClick(sender As Object, e As EventArgs)
        _hook.Stop()
        trayIcon.Visible = False
        Application.Exit()
    End Sub
#End Region

#Region "Hook Handlers"
    ''' <summary>
    ''' Click chuột (trái/phải/giữa) có thể làm con trỏ soạn thảo dời sang vị
    ''' trí khác hoặc tạo selection mới trong app đang gõ — hook bàn phím
    ''' không thể biết việc này. Phải xoá buffer ngay khi phát hiện click,
    ''' nếu không lần áp dấu/xoá lùi kế tiếp sẽ tính nhầm vào vị trí cũ.
    ''' (Bỏ qua sự kiện di chuột thuần — chỉ xử lý lúc nhấn nút.)
    ''' </summary>
    Private Sub HandleMouseActivity(sender As Object, e As MouseEventArgs)
        If Not _enabled Then Return
        If e.Button <> MouseButtons.None Then
            FlushBuffer(True)
        End If
    End Sub

    Private Sub HandleKeyDown(sender As Object, e As KeyEventArgs)
        If Not _enabled Then Return

        ' Chế độ Game: Enter dùng để MỞ/ĐÓNG khung chat (bật/tắt xử lý tiếng
        ' Việt). Kiểm tra này phải nằm TRƯỚC IsImeActive(), vì lúc chat đang
        ' đóng thì chính Enter là phím sẽ mở chat ra.
        ' KHÔNG set e.Handled → phím Enter thật vẫn gửi cho game như bình
        ' thường (để game tự mở/đóng/gửi khung chat của nó).
        If _gameModeEnabled AndAlso e.KeyCode = Keys.Return Then
            _chatOpenViaEnter = Not _chatOpenViaEnter
            If Not _chatOpenViaEnter Then
                _buf.Clear()
                _lastToneKey = Chr(0)
            End If
            UpdateGameStatusLabel()
        End If

        ' Esc trong khi khung chat đang mở → coi như hủy/đóng chat, không gõ nữa
        If _gameModeEnabled AndAlso _chatOpenViaEnter AndAlso e.KeyCode = Keys.Escape Then
            _chatOpenViaEnter = False
            _buf.Clear()
            _lastToneKey = Chr(0)
            UpdateGameStatusLabel()
        End If

        If Not IsImeActive() Then Return

        ' Win key kết hợp bất kỳ (SuppressKeyPress được tái dụng để báo Win key từ GlobalHook)
        If e.SuppressKeyPress AndAlso _buf.Length > 0 Then
            FlushBuffer(True)
            Return
        End If

        ' Alt kết hợp phím bất kỳ (Alt+Tab, Alt+F4, v.v.) → flush
        If e.Alt AndAlso _buf.Length > 0 Then
            FlushBuffer(True)
            Return
        End If

        ' Ctrl kết hợp phím bất kỳ (Ctrl+C, Ctrl+V, v.v.) → flush
        If e.Control AndAlso _buf.Length > 0 Then
            FlushBuffer(True)
            Return
        End If

        ' Shift + phím điều hướng / chọn vùng → flush
        If e.Shift AndAlso _buf.Length > 0 Then
            Select Case e.KeyCode
                Case Keys.Left, Keys.Right, Keys.Up, Keys.Down,
                     Keys.Home, Keys.End, Keys.Prior, Keys.Next,
                     Keys.Delete, Keys.Insert, Keys.Back
                    FlushBuffer(True)
            End Select
            Return
        End If

        Select Case e.KeyCode
            Case Keys.Back
                If _buf.Length > 0 Then
                    e.Handled = True
                    _buf.Remove(_buf.Length - 1, 1)
                    _lastToneKey = Chr(0)
                    DeleteChars(1)
                Else
                    e.Handled = False
                End If

            Case Keys.Space, Keys.Return, Keys.Tab
                If _buf.Length > 0 Then FlushBuffer(True)

            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down,
                 Keys.Home, Keys.End, Keys.Prior, Keys.Next,
                 Keys.Delete, Keys.Escape, Keys.Insert,
                 Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5,
                 Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10,
                 Keys.F11, Keys.F12, Keys.PrintScreen, Keys.Scroll,
                 Keys.Pause, Keys.NumLock, Keys.Capital
                If _buf.Length > 0 Then FlushBuffer(True)
        End Select
    End Sub

    Private Sub FlushBuffer(clearBuf As Boolean)
        If _buf.Length = 0 Then Return
        If clearBuf Then
            _buf.Clear()
            _lastToneKey = Chr(0)
        End If
    End Sub

    Private Sub HandleKeyPress(sender As Object, e As KeyPressEventArgs)
        If Not IsImeActive() Then Return

        Dim ch As Char = e.KeyChar
        If Char.IsControl(ch) Then Return

        Dim isDigit As Boolean = Char.IsDigit(ch)

        ' Ký tự không phải chữ/số → flush buffer rồi để phím gốc tự xử lý (không gửi lại)
        If Not Char.IsLetter(ch) AndAlso Not isDigit Then
            FlushBuffer(True)
            ' KHÔNG set e.Handled = True → phím gốc (Space, dấu câu, v.v.) tự pass qua
            Return
        End If

        ' Số: với Telex thì số không có ý nghĩa → flush buffer, để phím gốc tự pass qua.
        ' Với VNI thì số chính là phím chức năng (biến hình nguyên âm / dấu thanh) nên phải xử lý tiếp.
        If isDigit AndAlso _inputMethod = InputMethodType.Telex Then
            FlushBuffer(True)
            Return
        End If

        ' Từ đây: chữ cái (cả 2 kiểu) hoặc số (chỉ VNI) → IME xử lý, chặn phím gốc
        e.Handled = True

        _buf.Append(ch)
        Dim raw As String = _buf.ToString()
        Dim converted As String = Nothing

        ' Bảng biến hình nguyên âm/phụ âm theo kiểu gõ hiện tại
        Dim vowelMods As Dictionary(Of String, Char) =
            If(_inputMethod = InputMethodType.VNI, VniVowelModifiers, VowelModifiers)

        ' Bước 1: biến hình nguyên âm (Telex: aa/ee/oo/... ; VNI: a6/o7/d9/...)
        ' Trường hợp đặc biệt Telex: "uo" + w → "ươ" (cả 2 nguyên âm cùng biến hình),
        ' kể cả khi có âm cuối theo sau (uow→ươ, uoiw→ươi, uouw→ươu...).
        ' Nếu chỉ xét 2 ký tự cuối như bình thường thì "ow"→"ơ" sẽ biến uo+w
        ' thành "uơ" (sai) thay vì "ươ", và "iw"/"uw" sau "uo" không khớp gì cả
        ' (không đổi được) khi có âm cuối xen giữa, ví dụ "uoiw".
        If _inputMethod = InputMethodType.Telex AndAlso ch = "w"c AndAlso raw.Length >= 3 Then
            ' Tìm vị trí "uo" hoặc "Uo"/"UO" ngay trước phần đuôi (âm cuối, nếu có)
            ' bằng cách quét lùi từ trước ký tự 'w' vừa gõ để tìm cặp u+o liền kề
            ' chưa bị biến hình, với tối đa 1 ký tự nguyên âm cuối xen giữa (i/u).
            Dim beforeW As String = raw.Substring(0, raw.Length - 1)
            Dim uoIdx As Integer = -1
            For scan As Integer = beforeW.Length - 2 To 0 Step -1
                Dim c1 As Char = Char.ToLower(beforeW(scan))
                Dim c2 As Char = Char.ToLower(beforeW(scan + 1))
                If c1 = "u"c AndAlso c2 = "o"c Then
                    ' Loại trừ trường hợp "qu" là phụ âm đầu (vd "quow" không phải "uo" đôi)
                    If scan = 1 AndAlso Char.ToLower(beforeW(0)) = "q"c Then Continue For
                    uoIdx = scan
                    Exit For
                End If
            Next
            ' Phần đuôi sau "uo": chấp nhận nếu rỗng, hoặc là nguyên âm phụ i/u
            ' (cho ươi/ươu), hoặc là phụ âm cuối hợp lệ (c/ch/m/n/ng/nh/p/t)
            If uoIdx >= 0 Then
                Dim tail As String = beforeW.Substring(uoIdx + 2).ToLower()
                Dim tailOk As Boolean =
                    tail = "" OrElse tail = "i" OrElse tail = "u" OrElse
                    ValidFinalConsonants.Contains(tail)
                If Not tailOk Then uoIdx = -1
            End If
            If uoIdx >= 0 Then
                Dim newU As Char = If(Char.IsUpper(beforeW(uoIdx)), "Ư"c, "ư"c)
                Dim newO As Char = If(Char.IsUpper(beforeW(uoIdx + 1)), "Ơ"c, "ơ"c)
                converted = beforeW.Substring(0, uoIdx) & newU & newO & beforeW.Substring(uoIdx + 2)
                _buf.Clear() : _buf.Append(converted)
                _lastToneKey = Chr(0)
            End If
        End If

        If converted Is Nothing AndAlso raw.Length >= 2 Then
            Dim pair As String = raw.Substring(raw.Length - 2).ToLower()
            If vowelMods.ContainsKey(pair) Then
                Dim newCh As Char = vowelMods(pair)
                If Char.IsUpper(raw(raw.Length - 2)) Then newCh = Char.ToUpper(newCh)
                converted = raw.Substring(0, raw.Length - 2) & newCh
                _buf.Clear() : _buf.Append(converted)
                _lastToneKey = Chr(0)
            End If
        End If

        ' Bước 2: dấu thanh (Telex: s/f/r/x/j/z ; VNI: 1/2/3/4/5/0 → quy về cùng mã dấu nội bộ)
        Dim toneKey As Char = Chr(0)
        Dim isToneTrigger As Boolean = False
        If converted Is Nothing Then
            If _inputMethod = InputMethodType.VNI Then
                isToneTrigger = VniToneDigits.TryGetValue(ch, toneKey)
            Else
                If ToneKeys.Contains(Char.ToLower(ch)) Then
                    toneKey = Char.ToLower(ch)
                    isToneTrigger = True
                End If
            End If
        End If

        If isToneTrigger Then
            Dim syllable As String = raw.Substring(0, raw.Length - 1)

            If _lastToneKey <> Chr(0) AndAlso HasTone(syllable) Then
                If toneKey = _lastToneKey Then
                    ' Bỏ dấu: khôi phục chữ gốc + append đúng ký tự vừa gõ
                    ' (dùng "ch" (ký tự gốc, giữ đúng hoa/thường) thay vì "toneKey"
                    ' (luôn là chữ thường) để không làm sai hoa/thường khi gõ
                    ' toàn chữ HOA, ví dụ Caps Lock đang bật)
                    Dim appendChar As Char = ch
                    converted = StripTone(syllable) & appendChar.ToString()
                    _lastToneKey = Chr(0)
                Else
                    ' Đổi sang dấu khác
                    Dim stripped As String = StripTone(syllable)
                    Dim result As String = ApplyTone(stripped, toneKey)
                    If result IsNot Nothing Then
                        converted = result : _lastToneKey = toneKey
                    End If
                End If
            Else
                Dim result As String = ApplyTone(syllable, toneKey)
                If result IsNot Nothing Then
                    converted = result : _lastToneKey = toneKey
                End If
            End If

            If converted IsNot Nothing Then
                _buf.Clear() : _buf.Append(converted)
            Else
                ' QUAN TRỌNG: KHÔNG xoá ký tự vừa gõ khỏi _buf.
                ' Ký tự này vẫn được gửi ra app như bình thường ở khối render
                ' chung phía dưới (converted = Nothing → SendUnicodeString(ch)),
                ' nên phải giữ nó trong buffer để buffer luôn khớp 1:1 với nội
                ' dung đã hiển thị trên màn hình.
                ' (Bug cũ: xoá khỏi buf nhưng vẫn gửi ra màn hình → buffer bị
                ' lệch ngắn hơn màn hình 1 ký tự; lần áp dấu kế tiếp tính sai
                ' số ký tự cần xoá lùi → gây lỗi kiểu "Trình" bị render thành
                ' "TTình".)
                _lastToneKey = Chr(0)
            End If
        End If

        ' Render ra app
        Dim prevLen As Integer = raw.Length - 1
        If converted IsNot Nothing Then
            If prevLen > 0 Then DeleteChars(prevLen)
            SendUnicodeString(converted)
        Else
            SendUnicodeString(ch)
        End If
    End Sub
#End Region

#Region "Telex Engine"
    Private Function HasTone(syllable As String) As Boolean
        For Each c As Char In syllable
            If TonedToBase.ContainsKey(Char.ToLower(c)) Then Return True
        Next
        Return False
    End Function

    Private Function StripTone(syllable As String) As String
        Dim sb As New StringBuilder(syllable.Length)
        For Each c As Char In syllable
            Dim low As Char = Char.ToLower(c)
            Dim baseV As Char
            If TonedToBase.TryGetValue(low, baseV) Then
                sb.Append(If(Char.IsUpper(c), Char.ToUpper(baseV), baseV))
            Else
                sb.Append(c)
            End If
        Next
        Return sb.ToString()
    End Function

    ' ── Phụ âm cuối hợp lệ trong tiếng Việt ──────────────────────────────
    ' Chỉ: c, ch, m, n, ng, nh, p, t + nguyên âm (hoặc rỗng)
    Private ReadOnly ValidFinalConsonants As New HashSet(Of String)(
        New String() {"c", "ch", "m", "n", "ng", "nh", "p", "t"})

    ' ── Phụ âm đầu hợp lệ ────────────────────────────────────────────────
    Private ReadOnly ValidInitialConsonants As New HashSet(Of String)(
        New String() {
            "", "b", "c", "ch", "d", "đ", "g", "gh", "gi", "h",
            "k", "kh", "l", "m", "n", "ng", "ngh", "nh", "p", "ph",
            "qu", "r", "s", "t", "th", "tr", "v", "x"})

    ''' <summary>
    ''' Kiểm tra âm tiết có cấu trúc tiếng Việt hợp lệ không.
    ''' Tách: [phụ âm đầu] + [nhóm nguyên âm] + [phụ âm cuối]
    ''' Nếu không hợp lệ → không áp dấu, trả về ký tự gốc cho app.
    ''' </summary>
    ''' <summary>
    ''' Trả về độ dài phụ âm đầu (0 nếu âm tiết bắt đầu bằng nguyên âm).
    ''' Dùng chung cho cả IsValidVietnameseSyllable và FindTonePosition để
    ''' tránh các ký tự "nguyên-âm-giả" nằm trong phụ âm ghép (gi, qu) bị
    ''' tính lẫn vào nhóm nguyên âm thật khi xác định vị trí đặt dấu.
    ''' </summary>
    Private Function GetInitialConsonantLength(syllable As String) As Integer
        Dim s As String = syllable.ToLowerInvariant()
        For Each ic As String In New String() {"ngh", "gh", "gi", "ch", "kh", "ng", "nh", "ph", "th", "tr", "qu"}
            If s.StartsWith(ic) Then Return ic.Length
        Next
        If s.Length > 0 AndAlso Not IsVowelChar(s(0)) Then Return 1
        Return 0
    End Function

    Private Function IsValidVietnameseSyllable(syllable As String) As Boolean
        If String.IsNullOrEmpty(syllable) Then Return False
        Dim s As String = syllable.ToLowerInvariant()

        ' Tách phụ âm đầu (greedy, dài nhất trước)
        Dim initialLen As Integer = GetInitialConsonantLength(s)
        Dim rest As String = s.Substring(initialLen)

        ' Sau khi tách phụ âm đầu, phải còn nguyên âm
        If String.IsNullOrEmpty(rest) Then Return False
        If Not IsVowelChar(rest(0)) Then Return False

        ' Tách nhóm nguyên âm
        Dim vowelPart As New System.Text.StringBuilder()
        Dim idx As Integer = 0
        While idx < rest.Length AndAlso IsVowelChar(rest(idx))
            vowelPart.Append(rest(idx))
            idx += 1
        End While
        If vowelPart.Length = 0 Then Return False

        ' Phần còn lại là phụ âm cuối
        Dim finalPart As String = rest.Substring(idx)

        ' Phụ âm cuối phải rỗng hoặc hợp lệ
        If finalPart = "" Then Return True
        If ValidFinalConsonants.Contains(finalPart) Then Return True

        ' Cho phép phụ âm cuối chưa hoàn chỉnh (đang gõ dở: "ban" chưa gõ xong)
        ' Kiểm tra xem có phải prefix của phụ âm cuối hợp lệ không
        For Each fc As String In ValidFinalConsonants
            If fc.StartsWith(finalPart) Then Return True
        Next

        Return False
    End Function

    Private Function ApplyTone(syllable As String, toneKey As Char) As String
        If String.IsNullOrEmpty(syllable) Then Return Nothing
        ' Chỉ validate khi chưa có dấu — nếu đã có dấu thì đang toggle, cho phép strip
        Dim alreadyToned As Boolean = HasTone(syllable)
        If Not alreadyToned AndAlso Not IsValidVietnameseSyllable(syllable) Then Return Nothing
        Dim tp As Integer = FindTonePosition(syllable.ToLowerInvariant())
        If tp < 0 Then Return Nothing
        Dim lc As Char = syllable.ToLowerInvariant()(tp)
        Dim baseV As Char = GetBaseVowel(lc)
        If Not ToneTable.ContainsKey(baseV) Then Return Nothing
        If Not ToneTable(baseV).ContainsKey(toneKey) Then Return Nothing
        Dim newV As Char = ToneTable(baseV)(toneKey)
        If Char.IsUpper(syllable(tp)) Then newV = Char.ToUpper(newV)
        Dim sb As New StringBuilder(syllable)
        sb(tp) = newV
        Return sb.ToString()
    End Function

    Private Function FindTonePosition(syl As String) As Integer
        If String.IsNullOrEmpty(syl) Then Return -1

        ' Bỏ qua đúng số ký tự phụ âm đầu (vd "gi", "qu") để không lẫn
        ' nguyên âm giả của phụ âm ghép vào nhóm nguyên âm thật.
        Dim initialLen As Integer = GetInitialConsonantLength(syl)

        Dim vEnd As Integer = -1
        Dim i As Integer = syl.Length - 1
        While i >= initialLen
            If IsVowelChar(syl(i)) Then vEnd = i : Exit While
            i -= 1
        End While
        If vEnd < 0 Then Return -1
        i = vEnd
        While i >= initialLen AndAlso IsVowelChar(syl(i))
            i -= 1
        End While
        Dim vStart As Integer = i + 1
        Dim groupLen As Integer = vEnd - vStart + 1
        Dim groupBase As New StringBuilder(groupLen)
        For k As Integer = vStart To vEnd
            groupBase.Append(GetBaseVowel(syl(k)))
        Next
        Dim groupKey As String = groupBase.ToString()
        Dim relPos As Integer = 0
        If VowelGroupMainPos.TryGetValue(groupKey, relPos) Then
            If relPos < groupLen Then Return vStart + relPos
        End If
        If groupLen = 1 Then Return vStart
        Return vEnd
    End Function

    Private Function IsVowelChar(c As Char) As Boolean
        Return BaseVowels.Contains(c) OrElse TonedToBase.ContainsKey(c)
    End Function

    Private Function GetBaseVowel(c As Char) As Char
        Dim b As Char
        If TonedToBase.TryGetValue(c, b) Then Return b
        Return c
    End Function
#End Region

#Region "SendInput Helpers"
    Private Sub DeleteChars(count As Integer)
        If count <= 0 Then Return
        Dim inputs(count * 2 - 1) As Win32.INPUT
        For k As Integer = 0 To count - 1
            inputs(k * 2).type = Win32.INPUT_KEYBOARD
            inputs(k * 2).U.ki.wVk = Win32.VK_BACK
            inputs(k * 2).U.ki.dwFlags = 0
            inputs(k * 2 + 1).type = Win32.INPUT_KEYBOARD
            inputs(k * 2 + 1).U.ki.wVk = Win32.VK_BACK
            inputs(k * 2 + 1).U.ki.dwFlags = Win32.KEYEVENTF_KEYUP
        Next
        Win32.SendInput(CUInt(count * 2), inputs, Marshal.SizeOf(GetType(Win32.INPUT)))
    End Sub

    Private Sub SendVirtualKey(vk As UShort)
        Dim inputs(1) As Win32.INPUT
        inputs(0).type = Win32.INPUT_KEYBOARD
        inputs(0).U.ki.wVk = vk
        inputs(0).U.ki.dwFlags = 0
        inputs(1).type = Win32.INPUT_KEYBOARD
        inputs(1).U.ki.wVk = vk
        inputs(1).U.ki.dwFlags = Win32.KEYEVENTF_KEYUP
        Win32.SendInput(2UI, inputs, Marshal.SizeOf(GetType(Win32.INPUT)))
    End Sub

    Private Sub SendUnicodeString(text As String)
        If String.IsNullOrEmpty(text) Then Return
        Select Case _outputEncoding
            Case OutputEncodingType.TCVN3
                SendTCVN3String(text)
            Case OutputEncodingType.VNIWindows
                SendVNIWindowsString(text)
            Case Else
                SendUnicodeStringRaw(text)
        End Select
    End Sub

    ''' <summary>Gửi chuỗi Unicode thuần (KEYEVENTF_UNICODE) – chế độ mặc định.</summary>
    Private Sub SendUnicodeStringRaw(text As String)
        Dim inputs(text.Length * 2 - 1) As Win32.INPUT
        For k As Integer = 0 To text.Length - 1
            Dim sc As UShort = CUShort(AscW(text(k)))
            inputs(k * 2).type = Win32.INPUT_KEYBOARD
            inputs(k * 2).U.ki.wScan = sc
            inputs(k * 2).U.ki.dwFlags = Win32.KEYEVENTF_UNICODE
            inputs(k * 2 + 1).type = Win32.INPUT_KEYBOARD
            inputs(k * 2 + 1).U.ki.wScan = sc
            inputs(k * 2 + 1).U.ki.dwFlags = Win32.KEYEVENTF_UNICODE Or Win32.KEYEVENTF_KEYUP
        Next
        Win32.SendInput(CUInt(text.Length * 2), inputs, Marshal.SizeOf(GetType(Win32.INPUT)))
    End Sub

    ''' <summary>
    ''' Gửi chuỗi dưới dạng TCVN3 (byte đơn).
    ''' Mỗi ký tự Unicode → tra TCVN3Map → gửi WM_CHAR qua PostMessage vào cửa sổ foreground.
    ''' Ứng dụng đích phải đang dùng font TCVN3 (VNI-Times, .VnTime, v.v.) để hiển thị đúng.
    ''' </summary>
    Private Sub SendTCVN3String(text As String)
        Dim hWnd As IntPtr = Win32.GetForegroundWindow()
        For Each ch As Char In text
            Dim b As Byte
            If TCVN3Map.TryGetValue(ch, b) Then
                Win32.PostMessage(hWnd, Win32.WM_CHAR, CUInt(b), 0UI)
            Else
                ' ký tự không có trong bảng TCVN3 → gửi nguyên ASCII
                Win32.PostMessage(hWnd, Win32.WM_CHAR, CUInt(AscW(ch)), 0UI)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Gửi chuỗi dưới dạng VNI-Windows (CP1258).
    ''' Dùng Encoding.GetEncoding(1258) để chuyển; gửi từng byte qua WM_CHAR.
    ''' </summary>
    Private Sub SendVNIWindowsString(text As String)
        Dim hWnd As IntPtr = Win32.GetForegroundWindow()
        Dim cp1258 As System.Text.Encoding
        Try
            cp1258 = System.Text.Encoding.GetEncoding(1258)
        Catch
            ' Nếu hệ thống không cài CP1258, fallback về Unicode
            SendUnicodeStringRaw(text)
            Return
        End Try
        Dim bytes() As Byte = cp1258.GetBytes(text)
        For Each b As Byte In bytes
            Win32.PostMessage(hWnd, Win32.WM_CHAR, CUInt(b), 0UI)
        Next
    End Sub
#End Region

#Region "Init Tables"
    Private Sub InitTables()
        InitVowelModifiers()
        InitVniTables()
        InitToneTable()
        InitTonedToBase()
        InitVowelGroupMainPos()
        InitEncodingMaps()
    End Sub

    Private Sub InitVowelModifiers()
        VowelModifiers.Add("aa", "â"c)
        VowelModifiers.Add("aw", "ă"c)
        VowelModifiers.Add("ee", "ê"c)
        VowelModifiers.Add("oo", "ô"c)
        VowelModifiers.Add("ow", "ơ"c)
        VowelModifiers.Add("uw", "ư"c)
        VowelModifiers.Add("dd", "đ"c)
    End Sub

    Private Sub InitVniTables()
        ' Biến hình nguyên âm / phụ âm: ký tự + số ngay sau
        VniVowelModifiers.Add("a6", "â"c)
        VniVowelModifiers.Add("a8", "ă"c)
        VniVowelModifiers.Add("e6", "ê"c)
        VniVowelModifiers.Add("o6", "ô"c)
        VniVowelModifiers.Add("o7", "ơ"c)
        VniVowelModifiers.Add("u7", "ư"c)
        VniVowelModifiers.Add("d9", "đ"c)

        ' Số dấu thanh: 1 sắc, 2 huyền, 3 hỏi, 4 ngã, 5 nặng, 0 xoá dấu
        ' (quy về cùng mã dấu nội bộ s/f/r/x/j/z mà ToneTable đang dùng cho Telex)
        VniToneDigits.Add("1"c, "s"c)
        VniToneDigits.Add("2"c, "f"c)
        VniToneDigits.Add("3"c, "r"c)
        VniToneDigits.Add("4"c, "x"c)
        VniToneDigits.Add("5"c, "j"c)
        VniToneDigits.Add("0"c, "z"c)
    End Sub

    Private Sub InitToneTable()
        Dim add = Sub(base As Char, s As Char, f As Char, r As Char, x As Char, j As Char, z As Char)
            Dim d As New Dictionary(Of Char, Char)()
            d.Add("s"c, s) : d.Add("f"c, f) : d.Add("r"c, r)
            d.Add("x"c, x) : d.Add("j"c, j) : d.Add("z"c, z)
            ToneTable.Add(base, d)
        End Sub
        add("a"c, "á"c, "à"c, "ả"c, "ã"c, "ạ"c, "a"c)
        add("ă"c, "ắ"c, "ằ"c, "ẳ"c, "ẵ"c, "ặ"c, "ă"c)
        add("â"c, "ấ"c, "ầ"c, "ẩ"c, "ẫ"c, "ậ"c, "â"c)
        add("e"c, "é"c, "è"c, "ẻ"c, "ẽ"c, "ẹ"c, "e"c)
        add("ê"c, "ế"c, "ề"c, "ể"c, "ễ"c, "ệ"c, "ê"c)
        add("i"c, "í"c, "ì"c, "ỉ"c, "ĩ"c, "ị"c, "i"c)
        add("o"c, "ó"c, "ò"c, "ỏ"c, "õ"c, "ọ"c, "o"c)
        add("ô"c, "ố"c, "ồ"c, "ổ"c, "ỗ"c, "ộ"c, "ô"c)
        add("ơ"c, "ớ"c, "ờ"c, "ở"c, "ỡ"c, "ợ"c, "ơ"c)
        add("u"c, "ú"c, "ù"c, "ủ"c, "ũ"c, "ụ"c, "u"c)
        add("ư"c, "ứ"c, "ừ"c, "ử"c, "ữ"c, "ự"c, "ư"c)
        add("y"c, "ý"c, "ỳ"c, "ỷ"c, "ỹ"c, "ỵ"c, "y"c)
    End Sub

    Private Sub InitTonedToBase()
        Dim pairs() As String = {
            "áà ảãạ:a", "ắằẳẵặ:ă", "ấầẩẫậ:â",
            "éèẻẽẹ:e", "ếềểễệ:ê",
            "íìỉĩị:i",
            "óòỏõọ:o", "ốồổỗộ:ô", "ớờởỡợ:ơ",
            "úùủũụ:u", "ứừửữự:ư",
            "ýỳỷỹỵ:y"
        }
        For Each entry As String In pairs
            Dim parts() As String = entry.Split(":"c)
            Dim baseChar As Char = parts(1)(0)
            For Each c As Char In parts(0)
                If c <> " "c Then TonedToBase(c) = baseChar
            Next
        Next
    End Sub

    Private Sub InitVowelGroupMainPos()
        ' 1 nguyên âm
        For Each c As Char In "aăâeêiouưôơy"
            VowelGroupMainPos(c.ToString()) = 0
        Next

        ' 2 nguyên âm
        VowelGroupMainPos("ai") = 0 : VowelGroupMainPos("ao") = 0
        VowelGroupMainPos("au") = 0 : VowelGroupMainPos("ay") = 0
        VowelGroupMainPos("âu") = 0 : VowelGroupMainPos("ây") = 0
        VowelGroupMainPos("eo") = 0 : VowelGroupMainPos("êu") = 0
        VowelGroupMainPos("ia") = 0 : VowelGroupMainPos("iê") = 1
        VowelGroupMainPos("iu") = 0 : VowelGroupMainPos("oa") = 0
        VowelGroupMainPos("oe") = 0 : VowelGroupMainPos("oi") = 0
        VowelGroupMainPos("oo") = 0 : VowelGroupMainPos("ôi") = 0
        VowelGroupMainPos("ơi") = 0 : VowelGroupMainPos("ua") = 0
        VowelGroupMainPos("uâ") = 1 : VowelGroupMainPos("ue") = 1
        VowelGroupMainPos("ui") = 0 : VowelGroupMainPos("uo") = 0
        VowelGroupMainPos("uô") = 1 : VowelGroupMainPos("uơ") = 1
        VowelGroupMainPos("uy") = 0 : VowelGroupMainPos("ưa") = 0
        VowelGroupMainPos("ưi") = 0 : VowelGroupMainPos("ươ") = 1
        VowelGroupMainPos("ưu") = 0 : VowelGroupMainPos("yê") = 1
        VowelGroupMainPos("oê") = 0

        ' 3 nguyên âm
        VowelGroupMainPos("iêu") = 1 : VowelGroupMainPos("oai") = 1
        VowelGroupMainPos("oao") = 1 : VowelGroupMainPos("oay") = 1
        VowelGroupMainPos("oeo") = 1 : VowelGroupMainPos("uai") = 1
        VowelGroupMainPos("uao") = 1 : VowelGroupMainPos("uay") = 1
        VowelGroupMainPos("uôi") = 1 : VowelGroupMainPos("uơi") = 1
        VowelGroupMainPos("ươi") = 1 : VowelGroupMainPos("ươu") = 1
        VowelGroupMainPos("ưới") = 1 : VowelGroupMainPos("uya") = 2
        VowelGroupMainPos("uyu") = 1 : VowelGroupMainPos("yêu") = 1
    End Sub

    ''' <summary>
    ''' Khởi tạo bảng chuyển mã Unicode → TCVN3 (byte đơn, font ABC/ABC+).
    ''' Nguồn: TCVN 5712-1:1993 + phần mở rộng phổ biến (BKHN, VNI-Times…).
    ''' VNI-Windows dùng CP1258 nên không cần bảng riêng — dùng .NET Encoding.GetEncoding(1258).
    ''' </summary>
    Private Shared Sub InitEncodingMaps()
        ' ══════════════════════════════════════════════════════
        '  TCVN3 – chữ thường (font ABC)
        ' ══════════════════════════════════════════════════════
        ' --- a ---
        TCVN3Map("à"c) = &HA0 : TCVN3Map("á"c) = &HA1
        TCVN3Map("â"c) = &HA2 : TCVN3Map("ã"c) = &HA3
        TCVN3Map("ạ"c) = &HA4 : TCVN3Map("ă"c) = &HA8
        TCVN3Map("ầ"c) = &HA5 : TCVN3Map("ấ"c) = &HA6
        TCVN3Map("ẩ"c) = &HA7 : TCVN3Map("ẫ"c) = &HAA
        TCVN3Map("ậ"c) = &HAB : TCVN3Map("ằ"c) = &HAC
        TCVN3Map("ắ"c) = &HAD : TCVN3Map("ẳ"c) = &HAE
        TCVN3Map("ẵ"c) = &HAF : TCVN3Map("ặ"c) = &HA9
        ' --- e ---
        TCVN3Map("è"c) = &HB5 : TCVN3Map("é"c) = &HB6
        TCVN3Map("ê"c) = &HB7 : TCVN3Map("ẹ"c) = &HB8
        TCVN3Map("ề"c) = &HB9 : TCVN3Map("ế"c) = &HBA
        TCVN3Map("ể"c) = &HBB : TCVN3Map("ễ"c) = &HBC
        TCVN3Map("ệ"c) = &HBD
        ' --- i ---
        TCVN3Map("ì"c) = &HCC : TCVN3Map("í"c) = &HCD
        TCVN3Map("ỉ"c) = &HCE : TCVN3Map("ĩ"c) = &HCF
        TCVN3Map("ị"c) = &HD0
        ' --- o ---
        TCVN3Map("ò"c) = &HD5 : TCVN3Map("ó"c) = &HD6
        TCVN3Map("ô"c) = &HD7 : TCVN3Map("õ"c) = &HD8
        TCVN3Map("ọ"c) = &HD9 : TCVN3Map("ồ"c) = &HDA
        TCVN3Map("ố"c) = &HDB : TCVN3Map("ổ"c) = &HDC
        TCVN3Map("ỗ"c) = &HDD : TCVN3Map("ộ"c) = &HDE
        TCVN3Map("ơ"c) = &HDF : TCVN3Map("ờ"c) = &HE0
        TCVN3Map("ớ"c) = &HE1 : TCVN3Map("ở"c) = &HE2
        TCVN3Map("ỡ"c) = &HE3 : TCVN3Map("ợ"c) = &HE4
        ' --- u ---
        TCVN3Map("ù"c) = &HE5 : TCVN3Map("ú"c) = &HE6
        TCVN3Map("ũ"c) = &HE7 : TCVN3Map("ụ"c) = &HE8
        TCVN3Map("ư"c) = &HE9 : TCVN3Map("ừ"c) = &HEA
        TCVN3Map("ứ"c) = &HEB : TCVN3Map("ử"c) = &HEC
        TCVN3Map("ữ"c) = &HED : TCVN3Map("ự"c) = &HEE
        ' --- y ---
        TCVN3Map("ỳ"c) = &HEF : TCVN3Map("ý"c) = &HF0
        TCVN3Map("ỷ"c) = &HF1 : TCVN3Map("ỹ"c) = &HF2
        TCVN3Map("ỵ"c) = &HF3
        ' --- đ ---
        TCVN3Map("đ"c) = &HF4

        ' ══════════════════════════════════════════════════════
        '  TCVN3 – chữ HOA (font ABC+ / trang 2, byte &HC0–&HFF)
        '  Ánh xạ chữ hoa có dấu tiếng Việt → byte trong trang ABC+.
        '  Font TCVN3 thực tế thường ghép ABC + ABC+ vào 1 file, byte
        '  &HC0–&HFF của trang 1 (ABC) trùng với &HA0–&HDF của trang 2 (ABC+).
        '  Bảng dưới dùng quy ước phổ biến nhất (dùng trong Word/Excel VN cũ).
        ' ══════════════════════════════════════════════════════
        TCVN3Map("À"c) = &HC0 : TCVN3Map("Á"c) = &HC1
        TCVN3Map("Â"c) = &HC2 : TCVN3Map("Ã"c) = &HC3
        TCVN3Map("Ạ"c) = &HC4 : TCVN3Map("Ă"c) = &HC8
        TCVN3Map("Ầ"c) = &HC5 : TCVN3Map("Ấ"c) = &HC6
        TCVN3Map("Ẩ"c) = &HC7 : TCVN3Map("Ẫ"c) = &HCA
        TCVN3Map("Ậ"c) = &HCB : TCVN3Map("Ằ"c) = &H80
        TCVN3Map("Ắ"c) = &H81 : TCVN3Map("Ẳ"c) = &H82
        TCVN3Map("Ẵ"c) = &H83 : TCVN3Map("Ặ"c) = &HC9
        TCVN3Map("È"c) = &H84 : TCVN3Map("É"c) = &H85
        TCVN3Map("Ê"c) = &H86 : TCVN3Map("Ẹ"c) = &H87
        TCVN3Map("Ề"c) = &H88 : TCVN3Map("Ế"c) = &H89
        TCVN3Map("Ể"c) = &H8A : TCVN3Map("Ễ"c) = &H8B
        TCVN3Map("Ệ"c) = &H8C : TCVN3Map("Ì"c) = &H8D
        TCVN3Map("Í"c) = &H8E : TCVN3Map("Ỉ"c) = &H8F
        TCVN3Map("Ĩ"c) = &H90 : TCVN3Map("Ị"c) = &H91
        TCVN3Map("Ò"c) = &H92 : TCVN3Map("Ó"c) = &H93
        TCVN3Map("Ô"c) = &H94 : TCVN3Map("Õ"c) = &H95
        TCVN3Map("Ọ"c) = &H96 : TCVN3Map("Ồ"c) = &H97
        TCVN3Map("Ố"c) = &H98 : TCVN3Map("Ổ"c) = &H99
        TCVN3Map("Ỗ"c) = &H9A : TCVN3Map("Ộ"c) = &H9B
        TCVN3Map("Ơ"c) = &H9C : TCVN3Map("Ờ"c) = &H9D
        TCVN3Map("Ớ"c) = &H9E : TCVN3Map("Ở"c) = &H9F
        TCVN3Map("Ỡ"c) = &HFB : TCVN3Map("Ợ"c) = &HFC
        TCVN3Map("Ù"c) = &HFD : TCVN3Map("Ú"c) = &HFE
        TCVN3Map("Ũ"c) = &HFF : TCVN3Map("Ụ"c) = &HD1
        TCVN3Map("Ư"c) = &HD2 : TCVN3Map("Ừ"c) = &HD3
        TCVN3Map("Ứ"c) = &HD4 : TCVN3Map("Ử"c) = &HF5
        TCVN3Map("Ữ"c) = &HF6 : TCVN3Map("Ự"c) = &HF7
        TCVN3Map("Ỳ"c) = &HF8 : TCVN3Map("Ý"c) = &HF9
        TCVN3Map("Ỷ"c) = &HFA : TCVN3Map("Ỹ"c) = &HB4
        TCVN3Map("Ỵ"c) = &HB3 : TCVN3Map("Đ"c) = &HD0

        ' Ghi chú VNI-Windows: CP1258 đã được .NET hỗ trợ sẵn qua
        ' Encoding.GetEncoding(1258), không cần bảng thủ công.
        ' VNIWinMap chỉ dùng để fallback nếu hệ thống không có CP1258.
        ' (Để trống — SendVNIWindowsString tự gọi Encoding.GetEncoding)

        ' ── Dựng bảng đảo ngược byte TCVN3 → Unicode (dùng cho chuyển đổi mã) ──
        TCVN3ReverseMap.Clear()
        For Each kvp As KeyValuePair(Of Char, Byte) In TCVN3Map
            If Not TCVN3ReverseMap.ContainsKey(kvp.Value) Then
                TCVN3ReverseMap(kvp.Value) = kvp.Key
            End If
        Next
    End Sub
#End Region

#Region "Chuyển đổi mã (Code Conversion)"
    ''' <summary>
    ''' Chuyển đổi văn bản tiếng Việt qua lại giữa Unicode, TCVN3 (ABC/ABC+)
    ''' và VNI Windows (CP1258). Dùng chung cho cửa sổ FrmConvert và menu tray.
    ''' Văn bản ở dạng TCVN3/VNI "legacy" được biểu diễn bằng chuỗi có mã ký tự
    ''' 0-255 tương ứng đúng giá trị byte gốc (giống cách SendTCVN3String /
    ''' SendVNIWindowsString đang gửi WM_CHAR) — tức là cách văn bản "chữ lạ"
    ''' (gõ font cũ rồi bị copy ra ngoài, hoặc lưu trong file .txt không Unicode).
    ''' </summary>
    Public Shared Function ConvertVietnameseText(text As String, fromEnc As OutputEncodingType, toEnc As OutputEncodingType) As String
        If String.IsNullOrEmpty(text) OrElse fromEnc = toEnc Then Return text
        Dim unicodeText As String = LegacyToUnicode(text, fromEnc)
        Return UnicodeToLegacy(unicodeText, toEnc)
    End Function

    ''' <summary>Chuyển 1 bảng mã bất kỳ → Unicode chuẩn (không làm gì nếu đã là Unicode).</summary>
    Private Shared Function LegacyToUnicode(text As String, enc As OutputEncodingType) As String
        Select Case enc
            Case OutputEncodingType.TCVN3
                Dim sb As New StringBuilder(text.Length)
                For Each ch As Char In text
                    Dim code As Integer = AscW(ch)
                    Dim u As Char
                    If code >= 0 AndAlso code <= 255 AndAlso TCVN3ReverseMap.TryGetValue(CByte(code), u) Then
                        sb.Append(u)
                    Else
                        sb.Append(ch)
                    End If
                Next
                Return sb.ToString()

            Case OutputEncodingType.VNIWindows
                Try
                    Dim cp1258 As Encoding = Encoding.GetEncoding(1258)
                    Dim bytes(text.Length - 1) As Byte
                    For i As Integer = 0 To text.Length - 1
                        Dim code As Integer = AscW(text(i))
                        bytes(i) = CByte(If(code >= 0 AndAlso code <= 255, code, 63)) ' 63 = '?'
                    Next
                    Return cp1258.GetString(bytes)
                Catch
                    Return text ' không có CP1258 trên máy → trả nguyên văn
                End Try

            Case Else ' Unicode
                Return text
        End Select
    End Function

    ''' <summary>Chuyển từ Unicode chuẩn → 1 bảng mã legacy bất kỳ (TCVN3/VNI), giữ nguyên nếu đích là Unicode.</summary>
    Private Shared Function UnicodeToLegacy(text As String, enc As OutputEncodingType) As String
        Select Case enc
            Case OutputEncodingType.TCVN3
                Dim sb As New StringBuilder(text.Length)
                For Each ch As Char In text
                    Dim b As Byte
                    If TCVN3Map.TryGetValue(ch, b) Then
                        sb.Append(ChrW(b))
                    Else
                        sb.Append(ch) ' ký tự không dấu / không có trong bảng → giữ nguyên
                    End If
                Next
                Return sb.ToString()

            Case OutputEncodingType.VNIWindows
                Try
                    Dim cp1258 As Encoding = Encoding.GetEncoding(1258)
                    Dim bytes() As Byte = cp1258.GetBytes(text)
                    Dim sb As New StringBuilder(bytes.Length)
                    For Each b As Byte In bytes
                        sb.Append(ChrW(b))
                    Next
                    Return sb.ToString()
                Catch
                    Return text
                End Try

            Case Else ' Unicode
                Return text
        End Select
    End Function
#End Region

End Class

' ════════════════════════════════════════════════════════════
'  Cửa sổ "Chuyển đổi mã" – chuyển văn bản qua lại giữa
'  Unicode, TCVN3 (ABC/ABC+) và VNI Windows (CP1258).
'  Dùng lại MainForm.ConvertVietnameseText (Shared) để đảm bảo
'  khớp 100% với bảng mã mà bộ gõ đang dùng khi gửi phím.
' ════════════════════════════════════════════════════════════
Public Class FrmConvert
    Inherits Form

    Private cboFrom As ComboBox
    Private cboTo As ComboBox
    Private txtInput As TextBox
    Private txtOutput As TextBox
    Private btnConvert As Button
    Private btnSwap As Button
    Private btnPaste As Button
    Private btnCopy As Button
    Private btnClear As Button

    Public Sub New()
        BuildUI()
    End Sub

    Private Sub BuildUI()
        Me.Text = "Chuyển đổi mã – Unicode / TCVN3 / VNI Windows"
        Me.Size = New Size(560, 480)
        Me.FormBorderStyle = FormBorderStyle.FixedSingle
        Me.MaximizeBox = False
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.BackColor = Color.FromArgb(245, 245, 245)

        Dim lblFrom As New Label()
        lblFrom.Text = "Từ bảng mã:"
        lblFrom.Font = New Font("Segoe UI", 9)
        lblFrom.Location = New Point(20, 18)
        lblFrom.AutoSize = True
        Me.Controls.Add(lblFrom)

        cboFrom = New ComboBox()
        cboFrom.Font = New Font("Segoe UI", 9)
        cboFrom.Location = New Point(110, 15)
        cboFrom.Size = New Size(180, 24)
        cboFrom.DropDownStyle = ComboBoxStyle.DropDownList
        cboFrom.Items.AddRange(New Object() {"Unicode (UTF-8)", "TCVN3 (ABC/ABC+)", "VNI Windows (CP1258)"})
        cboFrom.SelectedIndex = 1 ' mặc định: TCVN3 → Unicode (trường hợp dùng nhiều nhất)
        Me.Controls.Add(cboFrom)

        btnSwap = New Button()
        btnSwap.Text = "⇄"
        btnSwap.Size = New Size(36, 26)
        btnSwap.Location = New Point(300, 14)
        btnSwap.FlatStyle = FlatStyle.Flat
        btnSwap.Font = New Font("Segoe UI", 10, FontStyle.Bold)
        AddHandler btnSwap.Click, AddressOf OnSwapClick
        Me.Controls.Add(btnSwap)

        Dim lblTo As New Label()
        lblTo.Text = "Sang bảng mã:"
        lblTo.Font = New Font("Segoe UI", 9)
        lblTo.Location = New Point(345, 18)
        lblTo.AutoSize = True
        Me.Controls.Add(lblTo)

        cboTo = New ComboBox()
        cboTo.Font = New Font("Segoe UI", 9)
        cboTo.Location = New Point(440, 15)
        cboTo.Size = New Size(85, 24)
        cboTo.DropDownStyle = ComboBoxStyle.DropDownList
        cboTo.Items.AddRange(New Object() {"Unicode", "TCVN3", "VNI"})
        cboTo.SelectedIndex = 0 ' mặc định: → Unicode
        Me.Controls.Add(cboTo)

        Dim lblInput As New Label()
        lblInput.Text = "Văn bản gốc:"
        lblInput.Font = New Font("Segoe UI", 9)
        lblInput.Location = New Point(20, 52)
        lblInput.AutoSize = True
        Me.Controls.Add(lblInput)

        txtInput = New TextBox()
        txtInput.Multiline = True
        txtInput.ScrollBars = ScrollBars.Vertical
        txtInput.Font = New Font("Segoe UI", 10)
        txtInput.Location = New Point(20, 72)
        txtInput.Size = New Size(505, 130)
        Me.Controls.Add(txtInput)

        btnPaste = New Button()
        btnPaste.Text = "Dán từ Clipboard"
        btnPaste.Size = New Size(150, 28)
        btnPaste.Location = New Point(20, 208)
        btnPaste.FlatStyle = FlatStyle.Flat
        AddHandler btnPaste.Click, AddressOf OnPasteClick
        Me.Controls.Add(btnPaste)

        btnClear = New Button()
        btnClear.Text = "Xoá"
        btnClear.Size = New Size(80, 28)
        btnClear.Location = New Point(178, 208)
        btnClear.FlatStyle = FlatStyle.Flat
        AddHandler btnClear.Click, AddressOf OnClearClick
        Me.Controls.Add(btnClear)

        btnConvert = New Button()
        btnConvert.Text = "Chuyển đổi ▼"
        btnConvert.Size = New Size(150, 32)
        btnConvert.Location = New Point(375, 206)
        btnConvert.FlatStyle = FlatStyle.Flat
        btnConvert.BackColor = Color.FromArgb(0, 120, 215)
        btnConvert.ForeColor = Color.White
        btnConvert.Font = New Font("Segoe UI", 9, FontStyle.Bold)
        btnConvert.FlatAppearance.BorderSize = 0
        AddHandler btnConvert.Click, AddressOf OnConvertClick
        Me.Controls.Add(btnConvert)

        Dim lblOutput As New Label()
        lblOutput.Text = "Kết quả:"
        lblOutput.Font = New Font("Segoe UI", 9)
        lblOutput.Location = New Point(20, 248)
        lblOutput.AutoSize = True
        Me.Controls.Add(lblOutput)

        txtOutput = New TextBox()
        txtOutput.Multiline = True
        txtOutput.ScrollBars = ScrollBars.Vertical
        txtOutput.ReadOnly = True
        txtOutput.BackColor = Color.White
        txtOutput.Font = New Font("Segoe UI", 10)
        txtOutput.Location = New Point(20, 268)
        txtOutput.Size = New Size(505, 130)
        Me.Controls.Add(txtOutput)

        btnCopy = New Button()
        btnCopy.Text = "Copy kết quả"
        btnCopy.Size = New Size(150, 28)
        btnCopy.Location = New Point(20, 404)
        btnCopy.FlatStyle = FlatStyle.Flat
        btnCopy.BackColor = Color.FromArgb(0, 153, 76)
        btnCopy.ForeColor = Color.White
        AddHandler btnCopy.Click, AddressOf OnCopyClick
        Me.Controls.Add(btnCopy)

        Dim lblNote As New Label()
        lblNote.Text = "Ghi chú: văn bản font cũ (TCVN3/VNI) cần được dán đúng nguyên byte," &
            vbCrLf & "không phải bị font hiện tại hiển thị sai thành ô vuông/dấu hỏi."
        lblNote.Font = New Font("Segoe UI", 8, FontStyle.Italic)
        lblNote.ForeColor = Color.Gray
        lblNote.Location = New Point(180, 404)
        lblNote.AutoSize = True
        Me.Controls.Add(lblNote)
    End Sub

    Private Function SelectedFromEnc() As MainForm.OutputEncodingType
        Select Case cboFrom.SelectedIndex
            Case 1 : Return MainForm.OutputEncodingType.TCVN3
            Case 2 : Return MainForm.OutputEncodingType.VNIWindows
            Case Else : Return MainForm.OutputEncodingType.Unicode
        End Select
    End Function

    Private Function SelectedToEnc() As MainForm.OutputEncodingType
        Select Case cboTo.SelectedIndex
            Case 1 : Return MainForm.OutputEncodingType.TCVN3
            Case 2 : Return MainForm.OutputEncodingType.VNIWindows
            Case Else : Return MainForm.OutputEncodingType.Unicode
        End Select
    End Function

    Private Sub OnConvertClick(sender As Object, e As EventArgs)
        Try
            txtOutput.Text = MainForm.ConvertVietnameseText(txtInput.Text, SelectedFromEnc(), SelectedToEnc())
        Catch ex As Exception
            MessageBox.Show("Lỗi khi chuyển đổi: " & ex.Message, "Chuyển đổi mã",
                MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Đảo chiều "Từ" ↔ "Sang", đồng thời đưa kết quả hiện tại lên ô gốc để chuyển ngược lại.</summary>
    Private Sub OnSwapClick(sender As Object, e As EventArgs)
        Dim fromIdx As Integer = cboFrom.SelectedIndex
        Dim toIdx As Integer = If(cboTo.SelectedIndex = 0, 0, cboTo.SelectedIndex)
        ' map cboTo (3 item: Unicode/TCVN3/VNI) → cboFrom (3 item cùng thứ tự)
        cboFrom.SelectedIndex = toIdx
        cboTo.SelectedIndex = fromIdx

        If txtOutput.Text.Length > 0 Then
            txtInput.Text = txtOutput.Text
            txtOutput.Clear()
        End If
    End Sub

    Private Sub OnPasteClick(sender As Object, e As EventArgs)
        Try
            If Clipboard.ContainsText() Then txtInput.Text = Clipboard.GetText()
        Catch
        End Try
    End Sub

    Private Sub OnCopyClick(sender As Object, e As EventArgs)
        Try
            If txtOutput.Text.Length > 0 Then Clipboard.SetText(txtOutput.Text)
        Catch
        End Try
    End Sub

    Private Sub OnClearClick(sender As Object, e As EventArgs)
        txtInput.Clear()
        txtOutput.Clear()
    End Sub
End Class

' ════════════════════════════════════════════════════════════
'  Entry Point – Single Instance
'  Nếu app đã chạy: tìm cửa sổ cũ (kể cả đang ẩn xuống tray),
'  hiện nó lên & đưa lên foreground, rồi thoát ngay thay vì
'  mở thêm 1 instance mới.
' ════════════════════════════════════════════════════════════
Module Program
    ' Tên mutex duy nhất cho app này (không trùng app khác)
    Private Const MUTEX_NAME As String = "VietnameseIME_Telex_2CongLC_SingleInstance"
    ' Phải khớp đúng Me.Text đặt trong BuildUI() của MainForm
    Private Const MAIN_WINDOW_TITLE As String = "Bộ gõ Tiếng Việt Telex"

    <STAThread>
    Sub Main()
        Dim createdNew As Boolean
        Using mutex As New Threading.Mutex(True, MUTEX_NAME, createdNew)
            If Not createdNew Then
                ' Đã có 1 phiên bản đang chạy → đánh thức cửa sổ cũ rồi thoát,
                ' không tạo MainForm mới
                ActivateExistingInstance()
                Return
            End If

            Try
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(False)
                Application.Run(New MainForm())
            Finally
                mutex.ReleaseMutex()
            End Try
        End Using
    End Sub

    ''' <summary>
    ''' Tìm cửa sổ chính của instance đang chạy (FindWindow vẫn tìm được
    ''' dù cửa sổ đang Hide() xuống tray), sau đó Restore + đưa lên foreground.
    ''' </summary>
    Private Sub ActivateExistingInstance()
        Dim hWnd As IntPtr = Win32.FindWindow(Nothing, MAIN_WINDOW_TITLE)
        If hWnd <> IntPtr.Zero Then
            Win32.ShowWindow(hWnd, Win32.SW_RESTORE)
            Win32.SetForegroundWindow(hWnd)
        End If
    End Sub
End Module
