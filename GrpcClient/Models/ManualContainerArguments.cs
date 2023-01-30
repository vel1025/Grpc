using System.IO;
using Grpc.Core;
using Grpc.Net.Client;


namespace GrpcClient.Models
{
    class ManualContainerArguments
    {
        public ManualContainerArguments(SubmissionInformation submissionInformation, int i)
        {
            SubmissionFiles = submissionInformation.SubmissionFiles.ToArray();
            Lang = submissionInformation.Lang.ToString();
            Hash = submissionInformation.InputStr.ToString();
            ContainerName = "manual" + Lang + i;
            AvailableLanguages = new string[]{
                "java11", "java17", "clang", "php", "python3"
            };
            IsSend = false;
            IsDoProcess = false;
            IsEndConnect = false;
            InputStream = null;
            ManualExecStream = null;
            RequestWriteAsync = (async (string str) =>
            {
                Console.Write(str);
                if (IsSend)
                {
                    await ManualExecStream.RequestStream.WriteAsync(new ExecutionOutput { ExecutionOutputStr = str });
                }
            });
            SetStreamWriter = ((StreamWriter streamWriter) =>
            {
                InputStream = streamWriter;
            });
        }
        public FileInformation[] SubmissionFiles { get; set; }
        public string Lang { get; set; }
        public string Hash { get; set; }
        public int SubmissionFileCount { get; set; }
        public string ContainerName { get; set; }
        public string[] AvailableLanguages { get; set; }
        public bool IsSend { get; set; }
        public bool IsDoProcess { get; set; }
        public bool IsEndConnect { get; set; }
        public StreamWriter? InputStream { get; set; }
        public AsyncDuplexStreamingCall<ExecutionOutput, ExecutionInput> ManualExecStream { get; set; }
        public Func<string, Task> RequestWriteAsync { get; set; }
        public Action<StreamWriter> SetStreamWriter { get; set; }
        private string[] _warningLang = new string[]{
            "java11", "clang"
        };
        public string ShowWarning()
        {
            if (_warningLang[0] == Lang)
            {
                return "sjisでコンパイルする場合はjavacs、UTF-8でコンパイルする場合はjavacuと入力してください\n";
            }
            if(_warningLang[1] == Lang){
                return "sjisでコンパイルする場合はgccs、UTF-8でコンパイルする場合はgccuと入力してください\n";
            }
            return "";
        }
    }
}