Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors

''' <summary>
''' Bọc quanh model ONNX ngữ cảnh (2 âm tiết trước -> phân phối xác suất âm
''' tiết tiếp theo). Dùng để so sánh xác suất giữa âm tiết người dùng vừa gõ
''' và các "ứng viên" dễ nhầm (s/x, tr/ch, d/gi/r, l/n...) — phát hiện lỗi
''' nhầm âm/dấu mà việc tra từ điển đơn thuần không bắt được, vì cả 2 từ đều
''' có nghĩa (VD: "chia sẻ" vs "chia sẽ", "con dao sắc" vs "con dao xắc").
'''
''' Model là 1 mạng nơ-ron rất nhỏ (kiểu NPLM: embed 2 từ ngữ cảnh -> hidden
''' -> softmax trên vocab ~4098 từ phổ biến nhất), train trên corpus Wikipedia
''' tiếng Việt. Chất lượng phụ thuộc vào việc train (xem README phần ONNX).
''' </summary>
Public Class OnnxContextChecker
    Implements IDisposable

    Private ReadOnly _session As InferenceSession
    Private ReadOnly _word2id As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    Private Const UNK_ID As Integer = 1

    ''' <summary>Cặp phụ âm đầu tiếng Việt dễ bị gõ/viết nhầm lẫn với nhau.
    ''' Chỉ đổi phần phụ âm đầu, giữ nguyên phần vần + dấu thanh phía sau.</summary>
    Private Shared ReadOnly SwapGroups As String()() = {
        New String() {"s", "x"},
        New String() {"tr", "ch"},
        New String() {"d", "gi", "r"},
        New String() {"l", "n"}
    }

    Public Sub New(modelPath As String, vocabPath As String)
        If Not File.Exists(modelPath) Then
            Throw New FileNotFoundException("Không tìm thấy model ONNX: " & modelPath)
        End If
        If Not File.Exists(vocabPath) Then
            Throw New FileNotFoundException("Không tìm thấy file vocab: " & vocabPath)
        End If

        Dim lines = File.ReadAllLines(vocabPath, Encoding.UTF8)
        For i = 0 To lines.Length - 1
            _word2id(lines(i)) = i
        Next

        _session = New InferenceSession(modelPath)
    End Sub

    Private Function IdOf(word As String) As Integer
        If String.IsNullOrEmpty(word) Then Return 0 ' <pad>
        Dim id As Integer
        If _word2id.TryGetValue(word, id) Then Return id
        Return UNK_ID
    End Function

    ''' <summary>Chạy model, trả về toàn bộ phân phối xác suất cho âm tiết kế
    ''' tiếp, dựa trên 2 âm tiết ngữ cảnh trước đó (dùng "" nếu chưa có đủ,
    ''' ví dụ mới bắt đầu gõ).</summary>
    Private Function PredictDistribution(prevPrev As String, prev As String) As Single()
        Dim tensor As New DenseTensor(Of Long)({1, 2})
        tensor(0, 0) = IdOf(prevPrev)
        tensor(0, 1) = IdOf(prev)

        Dim inputs As New List(Of NamedOnnxValue) From {
            NamedOnnxValue.CreateFromTensor("context", tensor)
        }

        Using results = _session.Run(inputs)
            Return results.First(Function(r) r.Name = "probs").AsTensor(Of Single)().ToArray()
        End Using
    End Function

    ''' <summary>Xác suất model gán cho 1 từ cụ thể, theo ngữ cảnh 2 từ trước.</summary>
    Public Function ProbabilityOf(prevPrev As String, prev As String, word As String) As Single
        Dim dist = PredictDistribution(prevPrev, prev)
        Dim id = IdOf(word)
        If id < 0 OrElse id >= dist.Length Then Return 0.0F
        Return dist(id)
    End Function

    ''' <summary>Sinh danh sách ứng viên dễ nhầm cho 1 âm tiết (đổi phụ âm đầu
    ''' theo các cặp thường gặp). Không lọc theo từ điển ở đây — việc đó do
    ''' nơi gọi hàm này tự làm, vì class này không biết về từ điển chính tả.</summary>
    Public Shared Function GenerateConfusionCandidates(word As String) As List(Of String)
        Dim candidates As New List(Of String)
        If String.IsNullOrEmpty(word) Then Return candidates

        Dim lower = word.ToLowerInvariant()
        For Each group In SwapGroups
            For Each prefix In group
                If lower.StartsWith(prefix, StringComparison.Ordinal) Then
                    Dim remainder = word.Substring(prefix.Length)
                    For Each alt In group
                        If alt <> prefix Then candidates.Add(alt & remainder)
                    Next
                    Exit For ' 1 âm tiết chỉ khớp 1 nhóm phụ âm đầu
                End If
            Next
        Next
        Return candidates
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _session IsNot Nothing Then _session.Dispose()
    End Sub
End Class
