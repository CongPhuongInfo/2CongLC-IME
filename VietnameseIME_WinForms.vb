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
#End Region

#Region "Controls"
    Private chkStartup As CheckBox
    Private chkMinimizeToTray As CheckBox
    Private lblStatus As Label
    Private btnToggle As Button
    Private trayIcon As NotifyIcon
    Private trayMenu As ContextMenuStrip
    Private trayToggleItem As ToolStripMenuItem
#End Region

#Region "IME State"
    Private _hook As GlobalHook
    Private _trayClickTimer As Timer
    Private _enabled As Boolean = True   ' bật/tắt tiếng Việt
    Private _buf As New StringBuilder()
    Private _lastToneKey As Char = Chr(0)
#End Region

#Region "Telex tables"
    Private ReadOnly VowelModifiers As New Dictionary(Of String, Char)()
    Private ReadOnly ToneKeys As New HashSet(Of Char)({"s"c, "f"c, "r"c, "x"c, "j"c, "z"c})
    Private ReadOnly ToneTable As New Dictionary(Of Char, Dictionary(Of Char, Char))()
    Private ReadOnly TonedToBase As New Dictionary(Of Char, Char)()
    Private ReadOnly BaseVowels As New HashSet(Of Char)("aăâeêiouưôơy".ToCharArray())
    Private ReadOnly VowelGroupMainPos As New Dictionary(Of String, Integer)()
#End Region

    ' ── Constructor ──────────────────────────────────────────
    Public Sub New()
        InitTables()
        BuildUI()
        BuildTrayIcon()
        LoadSettings()

        _hook = New GlobalHook(False, True)
        AddHandler _hook.KeyDown, AddressOf HandleKeyDown
        AddHandler _hook.KeyPress, AddressOf HandleKeyPress

        UpdateTrayIcon()
    End Sub

#Region "UI Builder"
    Private Sub BuildUI()
        Me.Text = "Bộ gõ Tiếng Việt Telex"
        Me.Size = New Size(320, 200)
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

        ' ── Separator ──
        Dim sep As New Label()
        sep.BorderStyle = BorderStyle.Fixed3D
        sep.Size = New Size(278, 2)
        sep.Location = New Point(20, 60)
        Me.Controls.Add(sep)

        ' ── Checkbox khởi động cùng hệ thống ──
        chkStartup = New CheckBox()
        chkStartup.Text = "Khởi động cùng Windows"
        chkStartup.Font = New Font("Segoe UI", 9)
        chkStartup.Location = New Point(20, 75)
        chkStartup.AutoSize = True
        AddHandler chkStartup.CheckedChanged, AddressOf OnStartupChanged
        Me.Controls.Add(chkStartup)

        ' ── Checkbox ẩn xuống tray ──
        chkMinimizeToTray = New CheckBox()
        chkMinimizeToTray.Text = "Ẩn xuống tray khi đóng cửa sổ"
        chkMinimizeToTray.Font = New Font("Segoe UI", 9)
        chkMinimizeToTray.Location = New Point(20, 105)
        chkMinimizeToTray.AutoSize = True
        AddHandler chkMinimizeToTray.CheckedChanged, AddressOf OnMinimizeToTrayChanged
        Me.Controls.Add(chkMinimizeToTray)

        ' ── Version label ──
        Dim lblVer As New Label()
        lblVer.Text = "v15062026  –  2CongLC"
        lblVer.ForeColor = Color.Gray
        lblVer.Font = New Font("Segoe UI", 7.5)
        lblVer.Location = New Point(20, 148)
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
    Private Sub HandleKeyDown(sender As Object, e As KeyEventArgs)
        If Not _enabled Then Return

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
        If Not _enabled Then Return

        Dim ch As Char = e.KeyChar
        If Char.IsControl(ch) Then Return

        ' Ký tự không phải chữ/số → flush buffer rồi để phím gốc tự xử lý (không gửi lại)
        If Not Char.IsLetter(ch) AndAlso Not Char.IsDigit(ch) Then
            FlushBuffer(True)
            ' KHÔNG set e.Handled = True → phím gốc (Space, dấu câu, v.v.) tự pass qua
            Return
        End If

        ' Từ đây chỉ còn chữ/số → IME xử lý, chặn phím gốc
        e.Handled = True

        _buf.Append(ch)
        Dim raw As String = _buf.ToString()
        Dim converted As String = Nothing

        ' Bước 1: biến hình nguyên âm
        If raw.Length >= 2 Then
            Dim pair As String = raw.Substring(raw.Length - 2).ToLower()
            If VowelModifiers.ContainsKey(pair) Then
                Dim newCh As Char = VowelModifiers(pair)
                If Char.IsUpper(raw(raw.Length - 2)) Then newCh = Char.ToUpper(newCh)
                converted = raw.Substring(0, raw.Length - 2) & newCh
                _buf.Clear() : _buf.Append(converted)
                _lastToneKey = Chr(0)
            End If
        End If

        ' Bước 2: dấu thanh
        If converted Is Nothing AndAlso ToneKeys.Contains(Char.ToLower(ch)) Then
            Dim toneKey As Char = Char.ToLower(ch)
            Dim syllable As String = raw.Substring(0, raw.Length - 1)

            If _lastToneKey <> Chr(0) AndAlso HasTone(syllable) Then
                If toneKey = _lastToneKey Then
                    ' Bỏ dấu: khôi phục chữ gốc + append tone key để ra "as", "is", "aj"...
                    converted = StripTone(syllable) & toneKey.ToString()
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
                _buf.Remove(_buf.Length - 1, 1)
                _lastToneKey = Chr(0)
                SendUnicodeString(ch)
                Return
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
    Private Function IsValidVietnameseSyllable(syllable As String) As Boolean
        If String.IsNullOrEmpty(syllable) Then Return False
        Dim s As String = syllable.ToLowerInvariant()

        ' Tách phụ âm đầu (greedy, dài nhất trước)
        Dim initial As String = ""
        Dim rest As String = s
        For Each ic As String In New String() {"ngh", "gh", "gi", "ch", "kh", "ng", "nh", "ph", "th", "tr", "qu"}
            If s.StartsWith(ic) Then
                initial = ic
                rest = s.Substring(ic.Length)
                Exit For
            End If
        Next
        If initial = "" Then
            ' thử phụ âm đơn
            If rest.Length > 0 AndAlso Not IsVowelChar(rest(0)) Then
                initial = rest(0).ToString()
                rest = rest.Substring(1)
            End If
        End If

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
        Dim vEnd As Integer = -1
        Dim i As Integer = syl.Length - 1
        While i >= 0
            If IsVowelChar(syl(i)) Then vEnd = i : Exit While
            i -= 1
        End While
        If vEnd < 0 Then Return -1
        i = vEnd
        While i >= 0 AndAlso IsVowelChar(syl(i))
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
#End Region

#Region "Init Tables"
    Private Sub InitTables()
        InitVowelModifiers()
        InitToneTable()
        InitTonedToBase()
        InitVowelGroupMainPos()
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
        VowelGroupMainPos("iu") = 0 : VowelGroupMainPos("oa") = 1
        VowelGroupMainPos("oe") = 1 : VowelGroupMainPos("oi") = 0
        VowelGroupMainPos("oo") = 0 : VowelGroupMainPos("ôi") = 0
        VowelGroupMainPos("ơi") = 0 : VowelGroupMainPos("ua") = 0
        VowelGroupMainPos("uâ") = 1 : VowelGroupMainPos("ue") = 1
        VowelGroupMainPos("ui") = 0 : VowelGroupMainPos("uo") = 0
        VowelGroupMainPos("uô") = 1 : VowelGroupMainPos("uơ") = 1
        VowelGroupMainPos("uy") = 1 : VowelGroupMainPos("ưa") = 0
        VowelGroupMainPos("ưi") = 0 : VowelGroupMainPos("ươ") = 1
        VowelGroupMainPos("ưu") = 0 : VowelGroupMainPos("yê") = 1
        VowelGroupMainPos("oê") = 1

        ' 3 nguyên âm
        VowelGroupMainPos("iêu") = 1 : VowelGroupMainPos("oai") = 1
        VowelGroupMainPos("oao") = 1 : VowelGroupMainPos("oay") = 1
        VowelGroupMainPos("oeo") = 1 : VowelGroupMainPos("uai") = 1
        VowelGroupMainPos("uao") = 1 : VowelGroupMainPos("uay") = 1
        VowelGroupMainPos("uôi") = 1 : VowelGroupMainPos("uơi") = 1
        VowelGroupMainPos("ươi") = 1 : VowelGroupMainPos("ươu") = 1
        VowelGroupMainPos("ưới") = 1 : VowelGroupMainPos("uya") = 2
        VowelGroupMainPos("uyu") = 1 : VowelGroupMainPos("yêu") = 1

        ' 4 nguyên âm
        VowelGroupMainPos("uyên") = 2 : VowelGroupMainPos("uyêt") = 2
    End Sub
#End Region

End Class

' ════════════════════════════════════════════════════════════
'  Entry Point
' ════════════════════════════════════════════════════════════
Module Program
    <STAThread>
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module
