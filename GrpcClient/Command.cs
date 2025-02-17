using System.Diagnostics;
using System;
using GrpcClient.Models;
using Con = System.Console;
using System.Text;
using System.IO;
using Grpc.Core;
using Grpc.Net.Client;
using Hnx8.ReadJEnc;
using System.Text.RegularExpressions;

namespace GrpcClient
{
    public class Command
    {

        /// <summary>
        /// コンテナ名、コンテナを生成するときにautoかmanualに言語、番号をつけて生成
        /// </summary>
        private string _containerName = "";
        /// <summary>
        /// 言語
        /// </summary>
        private string _lang = "";
        /// <summary>
        /// 自動実行用か手動実行用かのフラグ
        /// </summary>
        private bool _isAuto = false;
        private bool _isInput = true;
        /// <summary>
        /// 入力文字列
        /// </summary>
        private string _inputStr = "";
        /// <summary>
        /// 基本ディレクトリのパス
        /// </summary>
        private string _path = "/opt/Data";
        /// <summary>
        /// Ex2のダウンロードコントローラのAPI
        /// </summary>
        private string _ex2PlusUrl = "http://localhost:5556/api/GrpcDownload/";
        /// <summary>
        /// Ex2のダウンロードAPIのキー
        /// </summary>
        private string _apiKey = "?apiKey=grpcdesu";
        private string _generateDirectoryPath = "";
        /// <summary>
        /// 手動実行用のリクエストストリームへの送信のデリゲート
        /// </summary>
        private Func<string, Task>? _requestWriteAsync;
        /// <summary>
        /// ストリームライターに手動実行のプロセスのinputをセットするデリゲート
        /// </summary>
        private Action<StreamWriter>? _setStreamWriter;
        /// <summary>
        /// コンパイルしない言語の列
        /// </summary>
        /// <value></value>
        private string[] _notCompileLanguage = new string[]{
            "php", "python3"
        };
        /// <summary>
        /// コンパイルする言語の列
        /// </summary>
        /// <value></value>
        private string[] _compileLanguage = new String[]{
            "java11", "clang"
        };
        public Command(string containerName, string lang)
        {
            this._containerName = containerName;
            this._lang = lang;
        }
        /// <summary>
        /// コマンドクラスのコンストラクタ
        /// </summary>
        /// <param name="containerName">コンテナの名前</param>
        /// <param name="lang">言語</param>
        /// <param name="isAuto">自動実行フラグ</param>
        /// <param name="inputStr">入力文字列</param>
        public Command(string containerName, string lang, bool isAuto, string inputStr)
        {
            this._containerName = containerName;
            this._lang = lang;
            this._isAuto = isAuto;
            this._inputStr = inputStr;
        }
        /// <summary>
        /// コマンドクラスのコンストラクタ
        /// </summary>
        /// <param name="containerName">コンテナの名前</param>
        /// <param name="lang">言語</param>
        /// <param name="requestWriteAsync">リクエストストリームの送信用デリゲート</param>
        /// <param name="setStreamWriter">プロセスの入力ストリームをセットするデリゲート</param>
        public Command(string containerName, string lang, Func<string, Task> requestWriteAsync, Action<StreamWriter> setStreamWriter)
        {
            this._requestWriteAsync = requestWriteAsync;
            this._setStreamWriter = setStreamWriter;
            this._containerName = containerName;
            this._lang = lang;
        }
        /// <summary>
        /// 自動実行用プロセス
        /// </summary>
        /// <param name="fileInformations">ファイル情報の列</param>
        /// <returns>自動実行プロセスの実行結果</returns>
        public async Task<StandardCmd> AutoExecAsync(FileInformation[] fileInformations)
        {
            StandardCmd result = new();
            string mainFile = "";
            // ファイルが保存されるパスを指定
            string directoryPath = "./ExecuteFiles/" + _containerName + "/Data";
            // 5回試行して、エラーだったら何もせず抜ける
            for (int i = 0; i < 5; i++)
            {
                // 実行用環境の構築
                if ((result = await BuildExecuteEnvironmentAsync()).ExitCode == 0)
                {
                    // 送られてきたファイル情報を1つずつ読み取る
                    foreach (FileInformation fileInformation in fileInformations)
                    {
                        // ダウンロードするファイルのパスを生成
                        string filePath = directoryPath + "/" + fileInformation.FileName;
                        // ファイルをダウンロード
                        await DownloadFileAsync(_ex2PlusUrl + fileInformation.FileId + _apiKey, filePath);
                        // ダウンロードしたファイルがzipか判定
                        if (fileInformation.FileName.ToLower().Contains("zip"))
                        {
                            // zipを展開
                            if ((result = await UnzipAsync(filePath)).ExitCode == 0)
                            {
                                // zipファイルの中のメインファイルの要素を含むファイルをmainFileに格納
                                mainFile = Path.GetFileName(await JudgeUnzipMainFileAsync(directoryPath));
                            }
                        }
                        // zipじゃないかつメインファイルの要素を含んでいたらmainFileにファイル名を格納
                        else if (JudgeMainFile(filePath))
                        {
                            mainFile = fileInformation.FileName;
                        }
                    }
                    // mainFileが空か判定
                    if (mainFile == "")
                    {
                        // コンパイルしない言語なら最初に見つかったファイルをmianFileに格納、コンパイルする言語ならエラー
                        if (_notCompileLanguage.Any(value => value == _lang))
                        {
                            if ((mainFile = JudgeMainFile(fileInformations)) == "")
                            {
                                result = new StandardCmd("File not found.", "File not found.", -1);
                            }
                        }
                        else
                        {
                            result = new StandardCmd("File not found.", "File not found.", -1);
                        }
                    }
                    else
                    {
                        // コンパイルする言語ならコンパイル
                        if (_compileLanguage.Any(value => value == _lang))
                        {
                            result = await CompileAsync(directoryPath, mainFile);
                        }
                    }
                    // 実行
                    if (result.ExitCode == 0)
                    {
                        result = await ExecuteFileAsync(mainFile);
                        break;
                    }
                }
                await DiscardContainerAsync();
                await Task.Delay(100);
            }
            await DiscardContainerAsync();

            return result;
        }
        /// <summary>
        /// 手動実行プロセス
        /// </summary>
        /// <param name="fileInformations">ファイル情報の列</param>
        /// <returns>手動実行の実行結果</returns>
        public async Task<StandardCmd> ManualExecAsync(FileInformation[] fileInformations, bool isAvailableExtension, bool isZip)
        {

            StandardCmd result = new();
            string mainFile = "";
            string directoryPath = "./ExecuteFiles/" + _containerName + "/Data";
            if ((result = await BuildExecuteEnvironmentAsync()).ExitCode == 0)
            {
                foreach (FileInformation fileInformation in fileInformations)
                {
                    string filePath = directoryPath + "/" + fileInformation.FileName;
                    await DownloadFileAsync(_ex2PlusUrl + fileInformation.FileId + _apiKey, filePath);
                    if (isZip)
                    {
                        if ((result = await UnzipAsync(filePath)).ExitCode == 0)
                        {
                            mainFile = Path.GetFileName(await JudgeUnzipMainFileAsync(directoryPath));
                        }
                        else
                        {
                            return result;
                        }
                    }
                    else
                    {
                        if (JudgeMainFile(filePath))
                        {
                            mainFile = fileInformation.FileName;
                        }
                    }
                }
                if (_notCompileLanguage.Any(value => value == _lang) && mainFile == "")
                {
                    if ((mainFile = JudgeMainFile(fileInformations)) == "")
                    {
                        return new StandardCmd("Main File not found.", "Main File not found.", 0);
                    }
                }
                else
                {
                    if (_notCompileLanguage.Any(value => value == _lang) || (isZip && mainFile == "")) { }
                    else
                    {
                        if (mainFile != "")
                        {
                            result = await CompileAsync(directoryPath, mainFile);
                        }
                        else
                        {
                            return new StandardCmd("Main File not found.", "Main File not found.", 0);
                        }
                    }
                }
            }
            return result;
        }
        /// <summary>
        /// コンテナに必要なフォルダを作ってコンテナを立てる
        /// コンテナを立てる処理はバッシュファイルに記述(./PreparationFiles/bin/container.sh)
        /// </summary>
        /// <returns>実行結果</returns>
        public async Task<StandardCmd> BuildExecuteEnvironmentAsync()
        {
            StandardCmd mkdirResult = await MkdirExecuteFoldersAsync();
            if (mkdirResult.ExitCode != 0)
            {
                return mkdirResult;
            }
            StandardCmd cpResult = await CpExecuteFoldersAsync();
            if (cpResult.ExitCode != 0)
            {
                return cpResult;
            }
            StandardCmd buildResult = await BuildContainerAsync();
            if (buildResult.ExitCode != 0)
            {
                return buildResult;
            }
            return buildResult;
        }
        /// <summary>
        /// コンテナ関連のフォルダを削除してコンテナを閉じる
        /// </summary>
        /// <returns>実行結果</returns>
        public async Task DiscardContainerAsync()
        {
            StandardCmd rmContainerFiles = await RmContainerFilesAsync();
            StandardCmd stopResult = await StopContainerAsync();
            //自動でコンテナは削除される
            // StandardCmd RmContainerResult = await RmContainerAsync();
            StandardCmd rmFolderResult = await RmExecuteFoldersAsync();
            return;
        }
        public async Task<StandardCmd> BuildContainerAsync()
        {
            string build = "./PreparationFiles/bin/container.sh " + _lang + " " + _containerName;
            return await ExecuteAsync(build);
        }
        public async Task<StandardCmd> StopContainerAsync()
        {
            string cmdStr = "-c \"docker stop -t 0 " + _containerName + " \"";
            return await ExecuteAsync(cmdStr);
        }
        public async Task<StandardCmd> RmContainerAsync()
        {
            string cmdStr = "-c \"docker rm " + _containerName + " \"";
            return await ExecuteAsync(cmdStr);
        }
        public async Task<StandardCmd> MkdirExecuteFoldersAsync()
        {
            string mkdir = "./PreparationFiles/bin/mkdirExecuteFolder.sh " + _containerName;
            return await ExecuteAsync(mkdir);
        }
        public async Task<StandardCmd> CpExecuteFoldersAsync()
        {
            string cp = "./PreparationFiles/bin/cpExecuteFolder.sh " + _lang + " " + _containerName;
            return await ExecuteAsync(cp);
        }
        public async Task<StandardCmd> RmContainerFilesAsync()
        {
            string rmFiles = "-c \"docker exec -i -w /opt/Data " + _containerName + " bash -c 'rm -fR *'\"";
            return await ExecuteAsync(rmFiles);
        }
        public async Task<StandardCmd> RmExecuteFoldersAsync()
        {
            string rmFolder = "./PreparationFiles/bin/rmExecuteFolder.sh " + _containerName;
            return await ExecuteAsync(rmFolder);
        }
        public async Task<StandardCmd> CmdContainerAsync(string line)
        {
            if (line.Contains("cd"))
            {
                return await CdAsync(line);
            }
            else
            {
                string cmdStr = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c '" + line + "'\"";
                return await ExecuteAsync(cmdStr);
            }
        }
        public async Task<StandardCmd> CdAsync(string line)
        {
            string cmdStr = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c '" + line + " && pwd'\"";
            StandardCmd standardCmd = await ExecuteAsync(cmdStr);
            if (standardCmd.ExitCode == 0)
            {
                _path = standardCmd.Output.Trim();
            }
            return standardCmd;

        }
        public async Task<StandardCmd> CurrentDirectoryContainerAsync()
        {
            string pwd = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c 'basename `pwd`'\"";
            StandardCmd pwdResult = await ExecuteAsync(pwd);
            return pwdResult;
        }
        public async Task<StandardCmd> UnzipAsync(string filePath)
        {
            try
            {
                string unzipPath = Path.GetDirectoryName(filePath) ?? "";
                string unzip = "-c \"unzip -d " + unzipPath + " " + filePath + "\"";
                StandardCmd unzipResult = await ExecuteAsync(unzip);
                if (unzipResult.ExitCode != 0)
                {
                    return unzipResult;
                }
                string rmzip = "-c \"rm -fR " + filePath + "\"";
                StandardCmd rmzipResult = await ExecuteAsync(rmzip);
                if (rmzipResult.ExitCode != 0)
                {
                    return rmzipResult;
                }
                return rmzipResult;
            }
            catch (Exception)
            {
                return new StandardCmd("can't open zip file.", "can't open zip file.", -1);
            }
        }
        public async Task<StandardCmd> RmAsync(string fileName)
        {
            string cmdStr = "-c \"docker exec -i -w /opt/Data " + _containerName + " bash -c 'rm -Rf " + fileName + "\"";
            return await ExecuteAsync(cmdStr);
        }
        public async Task<string> TabCompletionAsync(string inputCommandString)
        {
            _isAuto = true;
            string result = "";
            //入力されている文字列から-tabを削除
            Regex removeTabCommandReg = new Regex("-tab" + @".*");
            inputCommandString = removeTabCommandReg.Replace(inputCommandString, "");

            // 入力されている文字をスペーズで区切る
            string[] inputStrings = inputCommandString.Split(" ");
            // 最後の文字列を取得
            string inputStringLastWord = inputStrings[inputStrings.Length - 1];
            Regex optionDecision = new Regex(@"^" + "-" + @"[a-zA-Z0-9]*");
            List<string> fileNameList = new List<string>();
            // 最後の文字列が"-"から始まってたらオプションと判断し、なにもしない
            if (!optionDecision.IsMatch(inputStringLastWord))
            {
                //inputStringsが一つならコマンド入力中と判定
                if (inputStrings.Length == 1 && inputStringLastWord != "")
                {
                    //コマンドを検索
                    string searchCommandCommand = "-c \"docker exec -i -w /usr/bin " + _containerName + " bash -c 'for x in ${PATH//:/ }; do ls -1 $x; done | sort | uniq | grep ^" + inputStringLastWord + "'\"";
                    StandardCmd searchCommandResult = await ExecuteAsync(searchCommandCommand);
                    if (searchCommandResult.ExitCode == 0 && searchCommandResult.Output.Split("\n")[0] != "")
                    {
                        result = "\n";
                        foreach (string commandCompletion in searchCommandResult.Output.TrimEnd().Split("\n"))
                        {
                            fileNameList.Add(commandCompletion);
                        }
                        foreach (string commandCompletion in fileNameList)
                        {
                            result += commandCompletion + "  ";
                        }
                        result.TrimEnd();
                    }
                }
                else
                {
                    //フォルダーまたはファイルを検索
                    string judgeFolderCommand = "";
                    if (inputStringLastWord == "")
                    {
                        judgeFolderCommand = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c 'ls -l | grep ^d'\"";
                    }
                    else
                    {
                        judgeFolderCommand = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c 'ls -ld " + inputStringLastWord + "* | grep ^d'\"";
                    }
                    StandardCmd judgeFolderResult = await ExecuteAsync(judgeFolderCommand);
                    List<string> folderNameList = new List<string>();
                    folderNameList = judgeFolderResult.Output.TrimEnd().Split("\n").ToList();
                    for (int i = 0; i < folderNameList.Count; i++)
                    {
                        //フォルダ名だけ取得
                        folderNameList[i] = folderNameList[i].Split(" ")[folderNameList[i].Split(" ").Length - 1];
                    }

                    string getFileNameCommand = "-c \"docker exec -i -w " + _path + " " + _containerName + " bash -c 'ls -d " + inputStringLastWord + "*'\"";
                    StandardCmd getFileNameResult = await ExecuteAsync(getFileNameCommand);
                    // 実行結果からファイル名を取り出し、リストに格納
                    foreach (string fileName in getFileNameResult.Output.TrimEnd().Split("\n"))
                    {
                        fileNameList.Add(fileName);
                    }
                    // リストの最初の要素がなかったら検索結果が0とみなし何もしない
                    if (fileNameList[0] != "")
                    {
                        result += "\n";
                        //フォルダなら最後に"/"をつけてファイル名から/を消す
                        for (int i = 0; i < fileNameList.Count; i++)
                        {
                            fileNameList[i] = Path.GetFileName(fileNameList[i]);
                            for (int j = 0; j < folderNameList.Count; j++)
                            {
                                if (fileNameList[i] == folderNameList[j] && Path.GetExtension(fileNameList[i]) == "")
                                {
                                    fileNameList[i] += "/";
                                }
                            }
                            result += (fileNameList[i] + "  ");
                        }
                        result.TrimEnd();
                    }
                }
                // リストの最初の要素がなかったら検索結果が0とみなし何もしない
                if (fileNameList.Count != 0 && fileNameList[0] != "")
                {
                    result += "\n[" + (await CurrentDirectoryContainerAsync()).Output.Trim() + "]# ";
                    //入力してあったものを入れなおす
                    if (inputStringLastWord == "")
                    {
                        result += inputCommandString;
                    }
                    else
                    {
                        for (int i = 0; i < inputStrings.Length - 1; i++)
                        {
                            result += (inputStrings[i] + " ");
                        }
                        result += inputStringLastWord;

                        //inputStringsの最後の要素とマッチするファイルまたはフォルダを検索
                        Comparison comparison = new Comparison();
                        string[] inputStringSlashByDelimiteds = inputStringLastWord.Split("/");
                        string inputStringSlashByDelimitedLastWord = inputStringSlashByDelimiteds[inputStringSlashByDelimiteds.Length - 1];
                        if (inputStringSlashByDelimitedLastWord == "")
                        {
                            result += comparison.FirstMatchString(fileNameList);
                        }
                        else
                        {
                            // もともと入力されていた部分とかぶるところを消してから結果に格納
                            result += comparison.FirstMatchString(fileNameList).Replace(inputStringSlashByDelimitedLastWord, "");
                        }
                    }
                }
            }
            _isAuto = false;
            return result;
        }
        public async Task<StandardCmd> CompileAsync(string directoryPath, string mainFile)
        {
            string compile = "-c \"docker exec -w /opt/bin " + _containerName + " bash ";
            string mainFilePath = "";
            string insideContainerFilePath = "";
            // 言語ごとに違う引数を渡す
            if (_lang == "java11")
            {
                string generateDirectoryPath = "";
                if ((generateDirectoryPath = await ReplaceJavaPackageAsync(directoryPath)) == "")
                {
                    mainFilePath = directoryPath + "/" + mainFile;
                    insideContainerFilePath = mainFile;
                }
                else
                {
                    mainFilePath = directoryPath + "/" + generateDirectoryPath;
                    insideContainerFilePath = generateDirectoryPath;
                    _generateDirectoryPath = generateDirectoryPath;
                }
            }
            else if (_lang == "clang")
            {
                mainFilePath = directoryPath + "/" + mainFile;
            }
            else
            {
                return new StandardCmd("compile error", "compile error", -1);
            }

            //エンコードを判定してコンパイル
            string encode = JudgeEncode(mainFilePath);
            if (encode.ToLower().Contains("utf-8"))
            {
                compile += "compile.sh ";
            }
            else if (encode.ToLower().Contains("sjis"))
            {
                compile += "compile_sjis.sh ";
            }
            else if (encode.ToLower().Contains("ascii"))
            {
                compile += "compile_ascii.sh ";
            }
            else
            {
                return new StandardCmd("compile error encoding " + encode, "compile errorencoding " + encode, -1);
            }
            // 言語ごとに違う引数を渡す
            if (_lang == "java11")
            {
                compile += insideContainerFilePath;
            }
            else if (_lang == "clang")
            {
                compile += GenerateCompileString(directoryPath, mainFile) + "\"";
            }
            else
            {
                return new StandardCmd("compile error", "compile error", -1);
            }

            StandardCmd compileResult = await ExecuteAsync(compile);
            if (compileResult.ExitCode == 0)
            {
                return compileResult;
            }
            return new StandardCmd("compile error", "compile error", -1);
        }
        public async Task<StandardCmd> ExecuteFileAsync(string fileName)
        {
            string cmdStr = "";
            if (_generateDirectoryPath == "")
            {
                string mainFilePath = Path.GetDirectoryName(fileName) == "" ? "" : "/" + Path.GetDirectoryName(fileName);
                string className = Path.GetFileNameWithoutExtension(fileName);
                cmdStr = "-c \"docker exec -i -w /opt/bin " + _containerName + " bash -c 'bash execute.sh " + className + "'\"";
            }
            else
            {
                //Package記述のあるJavaファイルに対応する
                string mainFilePath = Path.GetDirectoryName(fileName) == "" ? "" : "/" + Path.GetDirectoryName(fileName);
                string className = Path.GetFileNameWithoutExtension(fileName);
                string[] executeDirectoryPath = _generateDirectoryPath.Split("/");
                cmdStr = "-c \"docker exec -i -w /opt/Data " + _containerName + " bash -c 'bash /opt/bin/execute.sh " + Path.GetDirectoryName(_generateDirectoryPath) + "/" + className + "'\"";
            }

            return await ExecuteAsync(cmdStr);
        }
        /// <summary>
        /// コマンドの文字列を受け取り実行するプロセス
        /// </summary>
        /// <param name="cmdStr">コマンドの文字列</param>
        /// <returns>実行結果</returns>
        private async Task<StandardCmd> ExecuteAsync(string cmdStr)
        {
            Process? process = new();
            var task = Task.Run(async () =>
            {
                ProcessStartInfo p1 = new ProcessStartInfo("bash", cmdStr);

                // Console.WriteLine("bash " + cmdStr);

                p1.RedirectStandardOutput = true;
                p1.RedirectStandardError = true;
                p1.RedirectStandardInput = true;
                p1.UseShellExecute = false;
                process = Process.Start(p1);

                if (process == null)
                {
                    return new StandardCmd();
                }

                string str = "";

                if (_isAuto)
                {
                    str = await ProcessInOutAutoAsync(process);
                }
                else
                {
                    str = await ProcessInOutAsync(process);
                }


                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                int exitCode = process.ExitCode;

                process.Close();

                // Console.WriteLine("output:" + output);
                // Console.WriteLine("error:" + error);
                // Console.WriteLine("exitCode:" + exitCode);

                return new StandardCmd(str, error, exitCode);
            });
            return await task;
        }
        //手動実行用の実行プロセス
        private async Task<string> ProcessInOutAsync(Process process)
        {
            StreamWriter sw = process.StandardInput;
            StreamReader sr = process.StandardOutput;
            string str = "";
            if (_setStreamWriter == null)
            {
                return "error";
            }
            _setStreamWriter(sw);
            bool isTimeOut = true;

            var task = Task.Run(async () =>
            {
                str = await PrintOutAsync(process);
                isTimeOut = false;

            });
            int i = 0;
            while (i++ < 6000 && isTimeOut)
            {
                await Task.Delay(10);
            }
            if (6000 <= i)
            {
                process.Kill();
                return "this is time out";
            }

            return str;
        }
        private async Task<string> ProcessInOutAutoAsync(Process process)
        {
            string str = "";
            string[] inputStrs = _inputStr.Split("\n");
            // 入力用の文字列を整形する
            for (int n = 0; n < inputStrs.Length; n++)
            {
                inputStrs[n] = inputStrs[n].Trim();
            }
            int ch = -1;
            int i = 0;
            int timeOutCount = 0;
            if (_isInput) { timeOutCount = 50; } else { timeOutCount = 5000; }
            bool isTimeOut = true;

            var task = Task.Run(async () =>
            {
                StreamWriter sw = process.StandardInput;
                StreamReader sr = process.StandardOutput;
                try
                {
                    bool isFirst = true;
                    while (true)
                    {
                        var cts = new CancellationTokenSource();
                        CancellationToken token = cts.Token;
                        //非同期でインプットストリームへ書き込み、タイムアウトで書き込みキャンセル
                        var inputTask = Task.Run(async () =>
                        {
                            try
                            {
                                while (!token.IsCancellationRequested)
                                {
                                    // プログラムの一番最初は起動に時間がかかる場合があるからタイムアウトを長めに取る
                                    if (isFirst)
                                    {
                                        for (int i = 0; i < (timeOutCount * 4); i++)
                                        {
                                            await Task.Delay(1);
                                            if (token.IsCancellationRequested)
                                            {
                                                return;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //タイムアウトを1msごとに監視tokenがキャンセルになったら次へ50ミリ秒で入力待ちと判断し1行入力
                                        for (int i = 0; i < timeOutCount; i++)
                                        {
                                            await Task.Delay(1);
                                            if (token.IsCancellationRequested)
                                            {
                                                return;
                                            }
                                        }
                                    }

                                    if (i < inputStrs.Length)
                                    {
                                        if (inputStrs[i] == null) { return; }
                                        else
                                        {
                                            sw.WriteLine(inputStrs[i]);
                                            // Console.WriteLine(inputStrs[i]);
                                            str += inputStrs[i] + "\n";
                                        }
                                        i++;
                                    }
                                }
                            }
                            catch (NullReferenceException ex)
                            {
                                // Console.WriteLine(ex.ToString());
                            }

                            return;
                        });
                        if ((ch = sr.Read()) != -1)
                        {
                            str += (char)ch;
                            // Console.Write((char)ch);
                            cts.Cancel();
                        }
                        else
                        {
                            cts.Cancel();
                            break;
                        }
                        isFirst = false;
                        await inputTask;
                    }
                    isTimeOut = false;
                    return str;
                }
                catch (Exception ex)
                {
                    // 例外発生でプロセスを強制キル
                    // Console.WriteLine(ex.ToString());
                    process.Kill();
                    return "error";
                }
            });
            // 規定の時間を設定
            int j = 0;
            while (j++ < 1000 && isTimeOut)
            {
                await Task.Delay(10);
            }
            // 規定の時間たったらプロセスを強制キル
            if (1000 <= j)
            {
                process.Kill();
                return "this is time out";
            }
            return await task;
        }
        private async Task<string> PrintOutAsync(Process process)
        {
            int ch = 0;
            string str = "";
            StreamReader sr = process.StandardOutput;
            try
            {
                while ((ch = sr.Read()) != -1)
                {
                    str = str + (char)ch;
                    // Console.Write((char)ch);
                    await Task.Delay(1);
                    if (_requestWriteAsync == null)
                    {
                        return "error";
                    }
                    await _requestWriteAsync(((char)ch).ToString());
                }
            }
            catch (Exception)
            {
                return "error";
            }
            return str;
        }
        private async Task DownloadFileAsync(string url, string downloadPath)
        {
            try
            {
                // Console.WriteLine(url);
                // Console.WriteLine(downloadPath);
                // ダウンロード用URL設定
                var client = new HttpClient();
                var response = await client.GetAsync(url);
                //ステータスコードで成功したかチェック
                if (response.StatusCode != System.Net.HttpStatusCode.OK) return;
                // ファイルをダウンロード
                using var stream = await response.Content.ReadAsStreamAsync();
                using var outStream = File.Create(downloadPath);
                stream.CopyTo(outStream);
            }
            catch (Exception)
            {
                return;
            }
        }
        private string JudgeEncode(string filePath)
        {
            try
            {
                FileInfo file = new FileInfo(filePath);
                string name = "";
                // ファイル自動判別読み出しクラスを生成
                using (Hnx8.ReadJEnc.FileReader reader = new FileReader(file))
                {
                    // 判別読み出し実行。判別結果はReadメソッドの戻り値で把握できます
                    try
                    {
                        Hnx8.ReadJEnc.CharCode charCode = reader.Read(file);
                        // 戻り値のNameプロパティから文字コード名を取得できます
                        name = charCode.Name;
                    }
                    catch (System.NotSupportedException)
                    {
                        name = "sjis";
                    }
                }
                return name;
            }
            catch (Exception)
            {
                return "not Found File";
            }
        }
        private bool JudgeMainFile(string filePath)
        {
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string fileText = sr.ReadToEnd();
                    JudgeIsInputFile(fileText);
                    if (_lang == "java11")
                    {
                        if (fileText.ToLower().Contains("void main"))
                        {
                            return true;
                        }
                    }
                    if (_lang == "clang")
                    {
                        if (fileText.ToLower().Contains("main("))
                        {
                            return true;
                        }
                    }
                    if (_lang == "python3")
                    {
                        if (fileText.ToLower().Contains("if __name__ == '__main__'"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                return false;
            }
            return false;
        }
        private string JudgeMainFile(FileInformation[] fileInformations)
        {
            string mainFile = "";
            foreach (FileInformation fileInformation in fileInformations)
            {
                if (_lang == "python3" && Path.GetExtension(fileInformation.FileName) == ".py")
                {
                    mainFile = fileInformation.FileName;
                    break;
                }
                if (_lang == "php" && Path.GetExtension(fileInformation.FileName) == ".php")
                {
                    mainFile = fileInformation.FileName;
                    break;
                }
            }
            return mainFile;
        }
        private async Task<string> JudgeUnzipMainFileAsync(string directoryPath)
        {
            string mainFile = "";
            List<string> filePaths = GetFilePaths(directoryPath);
            //全ファイルのパスを取得して、ファイルにメインファイルの要素が含まれるか検索
            foreach (string filePath in filePaths)
            {
                if (JudgeMainFile(filePath))
                {
                    mainFile = filePath;
                }
            }
            // メインファイルがあって、メインファイルのパスがDataの直下じゃない場合移動
            if (mainFile != "" && Path.GetFileName(Path.GetDirectoryName(mainFile)) != "Data")
            {
                await MoveFileDirectoryAsync(mainFile, directoryPath);
            }
            else if (mainFile == "")
            {
                string firstFilePath = GetFirstFilePath(directoryPath);
                if (Path.GetFileName(Path.GetDirectoryName(firstFilePath)) != "Data")
                {
                    await MoveFileDirectoryAsync(firstFilePath, directoryPath);
                }
                mainFile = firstFilePath;
            }

            return mainFile;
        }
        private void JudgeIsInputFile(string text)
        {
            switch (_lang)
            {
                case "clang":
                    if (text.Contains("getchar()") || text.Contains(@"gets\([\w]+\)") || text.Contains(@"fgets\([\w]+\)") || text.Contains(@"scanf\([\w]\)"))
                    {
                        _isInput = true;
                    }
                    break;
                case "java11":
                    if (text.Contains("Scanner") || text.Contains("BufferedReader"))
                    {
                        _isInput = true;
                    }
                    break;
                case "python3":
                    if (text.Contains("input()"))
                    {
                        _isInput = true;
                    }
                    break;
                case "php":
                    if (text.Contains("fgets(STDIN)") || text.Contains(@"fgets\([\w]*\)"))
                    {
                        _isInput = true;
                    }
                    break;
                default:
                    _isInput = false;
                    break;
            }
            return;
        }
        private string GenerateCompileString(string diretoryPath, string mainFile)
        {
            List<string> filePaths = GetFilePaths(diretoryPath);
            string compile = "";
            foreach (string filePath in filePaths)
            {
                if (Path.GetFileName(filePath) == mainFile)
                {
                    compile += ("../Data/" + Path.GetFileNameWithoutExtension(mainFile) + " ");
                    break;
                }
            }
            foreach (string filePath in filePaths)
            {
                if (Path.GetFileName(filePath) == mainFile)
                {
                    compile += ("../Data/" + Path.GetFileName(mainFile) + " ");
                }
                else
                {
                    if (Path.GetExtension(filePath) == ".c")
                    {
                        compile += ("../Data/" + Path.GetFileName(filePath) + " ");
                    }
                }
            }
            compile.TrimEnd();
            return compile;
        }
        private List<string> GetFilePaths(string directoryPath)
        {
            List<string> filePaths = new List<string>();
            string[] files = Directory.GetFiles(directoryPath);
            foreach (string file in files)
            {
                filePaths.Add(file);
            }
            string[] directories = Directory.GetDirectories(directoryPath);
            foreach (string directory in directories)
            {
                foreach (string file in GetFilePaths(directory))
                {
                    filePaths.Add(file);
                }
            }
            return filePaths;
        }
        private string GetFirstFilePath(string directoryPath)
        {
            string[] files = Directory.GetFiles(directoryPath);
            foreach (string file in files)
            {
                switch (_lang)
                {
                    case "java11":
                        if (Path.GetExtension(file) == ".java")
                        {
                            return file;
                        }
                        break;
                    case "clang":
                        if (Path.GetExtension(file) == ".c")
                        {
                            return file;
                        }
                        break;
                    case "python3":
                        if (Path.GetExtension(file) == ".py")
                        {
                            return file;
                        }
                        break;
                    case "php":
                        if (Path.GetExtension(file) == ".php")
                        {
                            return file;
                        }
                        break;
                    default:
                        break;
                }
            }
            string[] directories = Directory.GetDirectories(directoryPath);
            foreach (string directory in directories)
            {
                return GetFirstFilePath(directory);
            }
            return "";
        }
        private async Task MoveFileDirectoryAsync(string filePath, string directoryPath)
        {
            // Console.WriteLine("filePath : " + filePath);
            string mv = "-c \"mv " + Path.GetDirectoryName(filePath) + "/* " + directoryPath;
            await ExecuteAsync(mv);
            string rm = "-c \"rm -fR " + Path.GetDirectoryName(filePath);
            await ExecuteAsync(rm);
        }
        private async Task<string> ReplaceJavaPackageAsync(string directoryPath)
        {
            string mainFilePath = "";
            try
            {
                List<string> filePaths = GetFilePaths(directoryPath);
                Regex search = new Regex(@"package ([a-zA-Z0-9\.]*);");
                Regex replaceDottoSlash = new Regex(@"\.");

                foreach (string filePath in filePaths)
                {
                    using (StreamReader sr = new StreamReader(filePath))
                    {
                        string fileGenerationPath = "";
                        string mvFile = "";

                        string fileText = sr.ReadToEnd();
                        if (fileText.ToLower().Contains("package"))
                        {
                            fileGenerationPath = (search.Match(fileText)).Value.ToString();
                            fileGenerationPath = search.Replace(fileGenerationPath, "$1");
                            fileGenerationPath = replaceDottoSlash.Replace(fileGenerationPath, "/");
                            string mkdir = "-c \"mkdir -p " + directoryPath + "/" + fileGenerationPath + "\"";
                            await ExecuteAsync(mkdir);
                            mvFile = "-c \"mv " + filePath + " " + directoryPath + "/" + fileGenerationPath + "/\"";
                            await ExecuteAsync(mvFile);
                            if (JudgeMainFile(directoryPath + "/" + fileGenerationPath + "/" + Path.GetFileName(filePath)))
                            {
                                mainFilePath = fileGenerationPath + "/" + Path.GetFileName(filePath);
                            }
                        }
                    }
                }
                return mainFilePath;
            }
            catch (Exception)
            {
                return mainFilePath;
            }
        }
    }
}