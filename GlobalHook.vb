Imports System
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Reflection
Imports System.Runtime
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms

#Region "How To Use "
'Step 1 : Dim g as GlobalHook
'Step 2 : Choice 1 in 3 Type 
' g = new GlobalHook() 'Enable Hook for all; 
' g = new GlobalHook(true,false) 'Enable Hook for Mouse; 
' g = new GlobalHook(false,true) 'Enable Hook for KeyBoard
'Step 3 : 
' Sub KeyPress(sender As Object, e As KeyPressEventArgs)  
' Sub KeyDown(Sender as Object, e as KeyEventArgs)
' Sub MouseEvent(Sender as Object, e as MouseEventArgs)
'Step 4 :
' AddHandler g.KeyPress, AddressOf KeyPress
' AddHandler g.OnMouseActivity, AddressOf MouseEvent
'Step 5:
' Dim c as Char = CChar(e.KeyChar) ' KeyPress 
' Dim m  = String.Format("x={0}  y={1} wheel={2}", e.X, e.Y, e.Delta) 'MouseEvent
#End Region
Public Class GlobalHook
    Inherits Object

#Region "Enum"
    Enum VK As Byte

        SPACE = &H20
        SHIFT = &H10
        CONTROL = &H11
        MENU = &H12
        PAUSE = &H13
        CAPITAL = &H14
        PRIOR = &H21 'Page UP
        [Next] = &H22 'Page DOWN
        [END] = &H23
        HOME = &H24

        LEFT = &H25 'LEFT ARROW key
        UP = &H26 'UP ARROW key
        RIGHT = &H27 'RIGHT ARROW key
        DOWN = &H28 'DOWN ARROW key

        NUMLOCK = &H90
        SCROLL = &H91
        ESCAPE = &H1B 'ESC Key
        SNAPSHOT = &H2C 'PRINT SCREEN key
        INSERT = &H2D
        DELETE = &H2E

        LSHIFT = &HA0 'Left SHIFT key
        RSHIFT = &HA1 'Right SHIFT key
        LCONTROL = &HA2
        RCONTROL = &HA3
        LALT = &HA4
        RALT = &HA5
    End Enum
    Enum WH As Integer

        MSGFILTER = -1
        JOURNALRECORD = 0
        JOURNALPLAYBACK = 1
        KEYBOARD = 2
        GETMESSAGE = 3
        CALLWNDPROC = 4
        CBT = 5
        SYSMSGFILTER = 6
        MOUSE = 7
        DEBUG = 9
        SHELL = 10
        FOREGROUNDIDLE = 11
        CALLWNDPROCRET = 12
        KEYBOARD_LL = 13
        MOUSE_LL = 14

    End Enum
    Enum WM As Integer
        ACTIVATE = &H6
        APPCOMMAND = &H319
        KEYDOWN = &H100
        KEYUP = &H101
        [Char] = &H102
        DEADCHAR = &H103
        HOTKEY = &H312
        KILLFOCUS = &H8
        LBUTTONDBLCLK = &H203
        LBUTTONDOWN = &H201
        LBUTTONUP = &H202
        MBUTTONDBLCLK = &H209
        MBUTTONDOWN = &H207
        MBUTTONUP = &H208
        MOUSEMOVE = &H200
        MOUSEWHEEL = &H20A
        RBUTTONDBLCLK = &H206
        RBUTTONDOWN = &H204
        RBUTTONUP = &H205
        SETFOCUS = &H7
        SYSKEYDOWN = &H104
        SYSKEYUP = &H105
        SYSDEADCHAR = &H107
        UNICHAR = &H109
    End Enum
#End Region
#Region "Events"
    Private events As New EventHandlerList()
    Public Custom Event KeyDown As KeyEventHandler
        AddHandler(ByVal value As KeyEventHandler)
            events.AddHandler("KeyDown", value)
        End AddHandler
        RemoveHandler(ByVal value As KeyEventHandler)
            events.RemoveHandler("KeyDown", value)
        End RemoveHandler
        RaiseEvent(ByVal sender As Object, ByVal e As KeyEventArgs)
            Dim eh As KeyEventHandler = TryCast(events("KeyDown"), KeyEventHandler)
            If eh IsNot Nothing Then eh.Invoke(sender, e)
        End RaiseEvent
    End Event
    Public Custom Event KeyPress As KeyPressEventHandler
        AddHandler(ByVal value As KeyPressEventHandler)
            events.AddHandler("KeyPress", value)
        End AddHandler
        RemoveHandler(ByVal value As KeyPressEventHandler)
            events.RemoveHandler("KeyPress", value)
        End RemoveHandler
        RaiseEvent(ByVal sender As Object, ByVal e As KeyPressEventArgs)
            Dim eh As KeyPressEventHandler = TryCast(events("KeyPress"), KeyPressEventHandler)
            If eh IsNot Nothing Then eh.Invoke(sender, e)
        End RaiseEvent
    End Event
    Public Custom Event KeyUp As KeyEventHandler
        AddHandler(ByVal value As KeyEventHandler)
            events.AddHandler("KeyUp", value)
        End AddHandler
        RemoveHandler(ByVal value As KeyEventHandler)
            events.RemoveHandler("KeyUp", value)
        End RemoveHandler
        RaiseEvent(ByVal sender As Object, ByVal e As KeyEventArgs)
            Dim eh As KeyEventHandler = TryCast(events("KeyUp"), KeyEventHandler)
            If eh IsNot Nothing Then eh.Invoke(sender, e)
        End RaiseEvent
    End Event
    Public Custom Event OnMouseActivity As MouseEventHandler
        AddHandler(Value As MouseEventHandler)
            events.AddHandler("OnMouseActivity", Value)
        End AddHandler
        RemoveHandler(Value As MouseEventHandler)
            events.RemoveHandler("OnMouseActivity", Value)
        End RemoveHandler
        RaiseEvent(sender As Object, e As MouseEventArgs)
            Dim eh As MouseEventHandler = TryCast(events("OnMouseActivity"), MouseEventHandler)
            If eh IsNot Nothing Then eh.Invoke(sender, e)
        End RaiseEvent
    End Event

#End Region
#Region "Windows API"
    Private Delegate Function HookProc(ByVal nCode As Integer,
                                      ByVal wParam As Integer,
                                      ByVal lParam As IntPtr) As Integer

    'https://msdn.microsoft.com/en-us/library/windows/desktop/ff468842(v=vs.85).aspx
    Private Declare Function UnhookWindowsHookEx Lib "user32.dll" (ByVal idHook As Integer) As Integer
    Private Declare Function SetWindowsHookEx Lib "user32.dll" Alias "SetWindowsHookExA" (ByVal idHook As Integer,
                                             ByVal lpfn As HookProc,
                                             ByVal hMod As IntPtr,
                                             ByVal dwThreadId As Integer) As Integer
    Private Declare Function CallNextHookEx Lib "user32.dll" (ByVal idHook As Integer,
                                           ByVal nCode As Integer,
                                           ByVal wParam As Integer,
                                           ByVal lParam As IntPtr) As Integer

    'https://msdn.microsoft.com/en-us/library/windows/desktop/ff468859(v=vs.85).aspx
    Private Declare Function GetKeyboardState Lib "user32.dll" (ByVal lpKeyState As Byte()) As Integer
    Private Declare Function GetKeyState Lib "user32.dll" (ByVal nVirtKey As Integer) As Short
    Private Declare Function ToAscii Lib "user32.dll" (ByVal uVirtKey As Integer,
                                    ByVal uScanCode As Integer,
                                    ByVal lpKeyState As Byte(),
                                    ByVal lpChar As Byte(),
                                    ByVal uFlags As Integer) As Integer
    Private Declare Function ToUnicode Lib "user32.dll" (ByVal wVirtKey As Integer,
                                                         ByVal wScanCode As Integer,
                                                         ByVal lpKeyState As Byte(),
                                                         ByVal pwszBuff As String,
                                                         ByVal cchBuff As Integer,
                                                         ByVal wFlags As Integer) As Integer
#End Region
#Region "Structs"
    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINT
        Public x As Integer
        Public y As Integer
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Private Structure KeyboardHookStruct
        Public vkCode As Integer
        Public scanCode As Integer
        Public flags As Integer
        Public time As Integer
        Public dwExtraInfo As Integer
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Private Structure MouseLLHookStruct
        Public pt As POINT
        Public mouseData As Integer
        Public flags As Integer
        Public time As Integer
        Public dwExtraInfo As Integer
    End Structure
#End Region
#Region "Start/Stop"
    Private KeyboardID, MouseID As Integer
    Private Shared KeyboardProc, MouseProc As HookProc
    Public Sub New()
        MouseID = 0
        KeyboardID = 0
        Start()
    End Sub

    Public Sub New(ByVal Mouse As Boolean, ByVal Keyboard As Boolean)
        MouseID = 0
        KeyboardID = 0
        Start(Mouse, Keyboard)
    End Sub

    Public Sub Start(ByVal Mouse As Boolean, ByVal KeyBoard As Boolean)
        If ((MouseID = 0) And Mouse) Then
            MouseProc = New HookProc(AddressOf Me.MouseCallBack)
            MouseID = SetWindowsHookEx(WH.MOUSE_LL,
                                          MouseProc,
                                          Marshal.GetHINSTANCE(Assembly.GetExecutingAssembly.GetModules()(0)),
                                          0)
            If (MouseID = 0) Then
                Dim errors As Integer = Marshal.GetLastWin32Error
                Me.Stop(True, False, False)
                Exception32(errors)
            End If
        End If
        If ((KeyboardID = 0) And KeyBoard) Then
            KeyboardProc = New HookProc(AddressOf Me.KeyBoardCallBack)
            KeyboardID = SetWindowsHookEx(WH.KEYBOARD_LL,
                                          KeyboardProc,
                                          Marshal.GetHINSTANCE(Assembly.GetExecutingAssembly.GetModules()(0)),
                                          0)
            If (KeyboardID = 0) Then
                Dim num2 As Integer = Marshal.GetLastWin32Error
                Me.Stop(False, True, False)
                Exception32(num2)
            End If
        End If
    End Sub
    Public Sub [Stop](ByVal Mouse As Boolean, ByVal KeyBoard As Boolean, ByVal ex As Boolean)
        If ((Me.MouseID > 0) And Mouse) Then
            Dim num As Integer = UnhookWindowsHookEx(Me.MouseID)
            Me.MouseID = 0
            If ((num = 0) And ex) Then
                Exception32(Marshal.GetLastWin32Error)
            End If
        End If
        If ((KeyboardID > 0) And KeyBoard) Then
            Dim num3 As Integer = UnhookWindowsHookEx(KeyboardID)
            KeyboardID = 0
            If ((num3 = 0) And ex) Then
                Exception32(Marshal.GetLastWin32Error)
            End If
        End If
    End Sub
    Public Sub Start()
        Me.Start(True, True)
    End Sub
    Public Sub [Stop]()
        Me.Stop(True, True, True)
    End Sub
    Protected Overrides Sub Finalize()
        Try
            Me.Stop(True, True, False)
        Finally
            MyBase.Finalize()
        End Try
    End Sub
    Private Sub Exception32(value As Integer)
        Throw New Win32Exception(value)
    End Sub
#End Region
#Region "Hooks"
    Private Function KeyBoardCallBack(ByVal nCode As Integer, ByVal wParam As Integer, ByVal lParam As IntPtr) As Integer

        Dim handled As Boolean = False

        If nCode > -1 AndAlso (events("KeyDown") IsNot Nothing OrElse events("KeyUp") IsNot Nothing OrElse events("KeyPress") IsNot Nothing) Then ' If (nCode >= 0) Then
            ' Bỏ qua keystroke do chính IME inject (KEYEVENTF_UNICODE hoặc LLKHF_INJECTED)
            Dim KHS_check As KeyboardHookStruct = DirectCast(Marshal.PtrToStructure(lParam, GetType(KeyboardHookStruct)), KeyboardHookStruct)
            Const LLKHF_INJECTED As Integer = &H10
            If (KHS_check.flags And LLKHF_INJECTED) <> 0 Then
                Return CallNextHookEx(KeyboardID, nCode, wParam, lParam)
            End If
            Dim KHS As KeyboardHookStruct = DirectCast(Marshal.PtrToStructure(lParam, GetType(KeyboardHookStruct)), KeyboardHookStruct)
            Dim control As Boolean = ((GetKeyState(VK.LCONTROL) And &H80) <> 0) OrElse ((GetKeyState(VK.RCONTROL) And &H80) <> 0)
            Dim shift As Boolean = ((GetKeyState(VK.LSHIFT) And &H80) <> 0) OrElse ((GetKeyState(VK.RSHIFT) And &H80) <> 0)
            Dim alt As Boolean = ((GetKeyState(VK.LALT) And &H80) <> 0) OrElse ((GetKeyState(VK.RALT) And &H80) <> 0)
            Dim capslock As Boolean = (GetKeyState(VK.CAPITAL) <> 0)
            Dim e As New KeyEventArgs(DirectCast(KHS.vkCode Or (If(control, CInt(Keys.Control), 0)) Or (If(shift, CInt(Keys.Shift), 0)) Or (If(alt, CInt(Keys.Alt), 0)), Keys))
            Select Case wParam
                Case WM.KEYDOWN, WM.SYSKEYDOWN
                    If events("KeyDown") IsNot Nothing Then
                        RaiseEvent KeyDown(Me, e)
                        handled = handled OrElse e.Handled
                    End If
                    Exit Select
                Case WM.KEYUP, WM.SYSKEYUP
                    If events("KeyUp") IsNot Nothing Then
                        RaiseEvent KeyUp(Me, e)
                        handled = handled OrElse e.Handled
                    End If
                    Exit Select
            End Select
            If wParam = WM.KEYDOWN AndAlso Not handled AndAlso Not e.SuppressKeyPress AndAlso events("KeyPress") IsNot Nothing Then

                Dim keyState As Byte() = New Byte(255) {}
                Dim inBuffer As Byte() = New Byte(1) {}
                GetKeyboardState(keyState)

                If ToAscii(KHS.vkCode, KHS.scanCode, keyState, inBuffer, KHS.flags) = 1 Then

                    Dim key As Char = CChar(ChrW(inBuffer(0)))
                    If (capslock Xor shift) AndAlso Char.IsLetter(key) Then
                        key = Char.ToUpper(key)
                    End If
                    Dim e2 As New KeyPressEventArgs(key)
                    RaiseEvent KeyPress(Me, e2)

                    handled = handled OrElse e2.Handled
                End If
            End If
        End If
        If handled Then
            Return 1
        End If
        Return CallNextHookEx(KeyboardID, nCode, wParam, lParam)
    End Function

    Private Function MouseCallBack(ByVal nCode As Integer, ByVal wParam As Integer, ByVal lParam As IntPtr) As Integer

        If (nCode > -1) AndAlso events("OnMouseActivity") IsNot Nothing Then

            Dim MSH As MouseLLHookStruct = DirectCast(Marshal.PtrToStructure(lParam, GetType(MouseLLHookStruct)), MouseLLHookStruct)

            Dim button As MouseButtons = MouseButtons.None
            Dim delta As Short = 0
            Dim click As Integer = 0

            Select Case wParam
                Case WM.LBUTTONDOWN
                    button = MouseButtons.Left
                    Exit Select
                Case WM.RBUTTONDOWN
                    button = MouseButtons.Right
                    Exit Select
                Case WM.MOUSEWHEEL
                    If MSH.mouseData > 0 Then
                        delta = 120
                    Else
                        delta = -120
                    End If
                    Exit Select
            End Select


            If (button > MouseButtons.None) Then
                If ((wParam = WM.LBUTTONDBLCLK) OrElse (wParam = WM.RBUTTONDBLCLK)) Then
                    click = 2
                Else
                    click = 1
                End If
            End If

            Dim e As New MouseEventArgs(button, click, MSH.pt.x, MSH.pt.y, delta)
            RaiseEvent OnMouseActivity(Me, e)
        End If

        Return CallNextHookEx(MouseID, nCode, wParam, lParam)
    End Function
#End Region

End Class


