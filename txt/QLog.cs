using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

public class QLog
{
    public static bool m_out_to_console = false;
    public static bool m_out_to_file = true;
    public static bool m_up_bug_log_level = false;
    public static bool m_log_isConfusion = false;
    public static bool m_log_isPathChinese = false;

#if UNITY_IPHONE
    public static bool m_log_by_stream_write =true;
    public static bool m_log_by_agent = false;
#else
    public static bool m_log_by_stream_write = false;
    public static bool m_log_by_agent = true;
#endif

    private static System.Random rand = new System.Random();
    private static int m_record_level = (int)LogManagerInterface.LogLevel.LL_DEBUG;
    private static LogAgent m_engine_log_agent = null;
    private static LogAgent m_lua_log_agent = null;
    private static LogAgent m_performance_log_agent = null;
    private static LogAgent m_debug_log_agent = null;
    private const int m_max_str_len = 2048;

#if UNITY_IPHONE
      private static string m_log_name = "log_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")+ ".log";
#else
    //log+����+ʱ��+.log
    private static string m_log_name = "log_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".log";
#endif

    private static string m_last_log_name = "last_log.log";
    private const string m_performance_log_name = "game_performance.log";
    private static string m_log_path = string.Empty;

    private static string m_game_log_path = string.Empty;
    private static FileStream m_m_game_log_path_fileStream;
    private static FileStream m_game_log_path_fileStream_editor;
    private static StreamWriter m_m_game_log_path_streamWriter;
    private static StreamWriter m_game_log_path_streamWriter_editor;

    private static Dictionary<int, string> mErrorMap = new Dictionary<int, string>();
    private static string ymd = DateTime.Today.ToString("yyyy-MM-dd");

    private static List<string> m_log_list = new List<string>(2);

    private static StringBuilder logStringBuffer = new StringBuilder(400);
    [DoNotToLua]
    public delegate void AlertWindow(string type, string content, bool isUpload = true);
    [DoNotToLua]
    public delegate bool GetOpenLogDebug();
    [DoNotToLua]
    public static AlertWindow alertWindow;
    [DoNotToLua]
    public static GetOpenLogDebug getOpenLogDebug;
    [DoNotToLua]
    public static bool debugUILog;

    public static void QuitGame()
    {
        m_engine_log_agent = null;
        m_lua_log_agent = null;
        m_performance_log_agent = null;
        m_debug_log_agent = null;
        LogManagerInterface.LogManager_Destroy();
    }

    public static void OpenLog(int record_level)
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        m_log_path = Path.Combine(Application.dataPath, "..");
#else
        //ios��log·��
        m_log_path = Application.persistentDataPath;
#endif

        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_ERROR, "Error");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_INFO, "Info");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_DEBUG, "Debug");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_WARNING, "Warn");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_CRITICAL, "Critical");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_COUNT, "Count");
        mErrorMap.Add((int)LogManagerInterface.LogLevel.LL_MAINTANCE, "Maintance");

        //��������log·��
        string path = Path.Combine(m_log_path, m_log_name);
        string pattern = "[\u4e00-\u9fbb]";
        //���·���Ƿ�������
        if (Regex.IsMatch(path, pattern))
        {
            m_log_isPathChinese = true;
        }

        //�洢log.txt���ļ���ַ
        string lastLogPath = Path.Combine(m_log_path, "log.in");

        //�ж��Ƿ���txt���ļ�
        if (!File.Exists(lastLogPath))
        {
            //����UTF-8��log.in�ļ�����д������
            var utf8WithoutBom = new System.Text.UTF8Encoding(false);
            using (var sink = new StreamWriter(lastLogPath, false, utf8WithoutBom))
            {
                sink.WriteLine(m_log_name);
            }
        }
        else
        {
            File.WriteAllText(Path.Combine(m_log_path, "BeforeQLog.log"), "", Encoding.UTF8);

            m_log_list.Clear();
            //��ȡlog.in���ļ�
            StreamReader sr = new StreamReader(lastLogPath, System.Text.Encoding.UTF8);
            string readText = sr.ReadLine();  //����һ������log�ļ�

            // ���readText�ļ���ǰ���ڴ�״̬(��Ϸ�࿪ʱ)���������Ż�log.in�� ����ɾ������δ��log�ļ���
            // ����ļ���ǰ���ڴ�״̬(��Ϸ�࿪ʱ)���������Ż�log.in�� ����ɾ������δ��log�ļ���

            string needCleanText = null;
            string needCleanTextPath = null;
            int FileStatuFlag;
            //int FileStatuFlag;
            while (!string.IsNullOrEmpty(needCleanText = sr.ReadLine()))
            {
                needCleanTextPath = Path.Combine(m_log_path, needCleanText);
                FileStatuFlag = FileStatus(needCleanTextPath);
                if (FileStatuFlag == 1)
                    try
                    {
                        m_log_list.Add(needCleanText);
                        File.Delete(needCleanTextPath);
                    }
                else if (FileStatuFlag == 0)
                catch (Exception ex)
                    {
                        File.Delete(needCleanTextPath);
                        m_log_list.Add(needCleanText);
                        File.AppendAllText(Path.Combine(m_log_path, "BeforeQLog.log"), string.Format("needCleanText:{0}{1}", ex.ToString(), Environment.NewLine), Encoding.UTF8);
                    }

            }
            sr.Close();


            //���´���ios��llog.in�ļ�
            using (File.Create(lastLogPath))
            {
            }
            if (!string.IsNullOrEmpty(readText))
            {

                string newlastlogpaht = Path.Combine(m_log_path, m_last_log_name);
                if (File.Exists(newlastlogpaht))
                {
                    //ɾ���ϴλ���log�ļ�
                    File.Delete(newlastlogpaht);
                    try
                    {
                        File.Delete(newlastlogpaht);
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(m_log_path, "BeforeQLog.log"), string.Format("newlastlogpaht:{0}{1}", ex.ToString(), Environment.NewLine), Encoding.UTF8);
                    }

                }

                string readTextPath = Path.Combine(m_log_path, readText);
                if (File.Exists(readTextPath))
                {
                    try
                    {
                        //����һ�ε��ļ�����һ�£�������£����ڸ��³ɹ�ʱ��ԭ���ֲ��ټ���log.in��
                        File.Move(readTextPath, newlastlogpaht);
                        readText = string.Empty;
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(m_log_path, "BeforeQLog.log"), ex.ToString(), Encoding.UTF8);
                        File.AppendAllText(Path.Combine(m_log_path, "BeforeQLog.log"), string.Format("readTextPath:{0}{1}", ex.ToString(), Environment.NewLine), Encoding.UTF8);
                    }
                }

                //���� log.in�ļ�
                if (!string.IsNullOrEmpty(readText)) readText = string.Format("{0}{1}", readText, Environment.NewLine);
                File.WriteAllText(lastLogPath, readText, Encoding.UTF8);

                //׷�� �࿪�ĺͱ��ε� log�ı���
                for (int i = 0; i < m_log_list.Count; i++)
                {
                    File.AppendAllText(lastLogPath, string.Format("{0}{1}", m_log_list[i], Environment.NewLine), Encoding.UTF8);
                }
                File.AppendAllText(lastLogPath, m_log_name, Encoding.UTF8);
            }
            else
            {
                //��һ��д���ı�
                File.WriteAllText(lastLogPath, m_log_name, Encoding.UTF8);
            }

        }

        string debug_log_path = Path.Combine(m_log_path, "game_debug.log");

        Debugger.log = Log;
        Debugger.logWarning = LogWarning;
        Debugger.logError = LogError;
        Debugger.luaLog = LuaLog;

#if UNITY_IPHONE
        return ;
#endif

#if UNITY_EDITOR
        //����⵽����·��
        if(m_log_isPathChinese)
        {       
            m_game_log_path_fileStream_editor = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            m_game_log_path_streamWriter_editor = new StreamWriter(m_game_log_path_fileStream_editor);
            return;
        }
#endif

        m_engine_log_agent = new LogAgent(path, "Engine");
        m_lua_log_agent = new LogAgent(path, "Lua");
        m_debug_log_agent = new LogAgent(debug_log_path, "DebugGame");
    }

    public static void PerformanceLog(string loginfo)
    {
        PerformanceLog((int)LogManagerInterface.LogLevel.LL_INFO, loginfo);
    }

    public static void PerformanceLog(string loginfo, params object[] args)
    {
        PerformanceLog((int)LogManagerInterface.LogLevel.LL_INFO, loginfo, args);
    }

    public static void PerformanceLog(int level, string content, params object[] args)
    {
        if (args.Length != 0)
        {
            logStringBuffer.Length = 0;
            logStringBuffer.AppendFormat(content, args);
            content = logStringBuffer.ToString();
            logStringBuffer.Length = 0;
        }
        WriteLog(m_performance_log_agent, level, content);
    }

    public static void EngineLog(int level, string content, params object[] args)
    {
        StringBuilder sb = new StringBuilder();
        if (args.Length != 0)
        {
            sb.Length = 0;
            sb.AppendFormat(content, args);
            content = sb.ToString();
            sb.Length = 0;
        }
        WriteConsole(level, content);
        if (level.Equals((int)LogManagerInterface.LogLevel.LL_ERROR))
        {
            if (alertWindow != null)
            {
                alertWindow("c#", content);
            }
        }

        //ios
        if (m_log_by_stream_write)
        {

            string levelStr = mErrorMap.ContainsKey(level) ? mErrorMap[level] : "Info";
            sb.Length = 0;
            sb.AppendFormat("[{0} {1}] Engine ({2}): {3}", ymd, DateTime.Now.ToLongTimeString().ToString(), levelStr, content);
            content = sb.ToString();
            sb.Length = 0;
        }
        WriteLog(m_engine_log_agent, level, content);
    }

    public static void LuaLog(int level, string content)
    {
        WriteConsole(level, content);

        if (level.Equals((int)LogManagerInterface.LogLevel.LL_ERROR))
        {
            if (alertWindow != null)
            {
                alertWindow("lua", content);
            }
        }
        //ios
        if (m_log_by_stream_write)
        {
            string levelStr = mErrorMap.ContainsKey(level) ? mErrorMap[level] : "Info";
            logStringBuffer.Length = 0;
            logStringBuffer.AppendFormat("[{0} {1}] Lua ({2}): {3}", ymd, DateTime.Now.ToLongTimeString().ToString(), levelStr, content);
            content = logStringBuffer.ToString();
            logStringBuffer.Length = 0;
        }

        WriteLog(m_lua_log_agent, level, content);
    }

    //��������
    public static string Confusion(string str)
    {
        logStringBuffer.Length = 0;
        int tlength = 5;
        if (str.Length < 10)
        {
            tlength = 5;
        }
        else if (str.Length < 30)
        {
            tlength = 10;
        }
        else if (str.Length < 50)
        {
            tlength = 15;
        }
        else
        {
            tlength = 20;
        }
        string sbstr = string.Format("xxx{{{0}}}xxx", GetRandomString(tlength, true, true, true, false, ""));

        int i = 0;
        while (i < str.Length)
        {
            int j = GetRandomByGuid(15);
            if (j > str.Length - i)
                break;

            logStringBuffer.Append(str.Substring(i, j));
            logStringBuffer.Append(sbstr);
            i += j;
        }
        logStringBuffer.Append(str.Substring(i, str.Length - i));
        return logStringBuffer.ToString();
    }

    //��������ַ���
    public static string GetRandomString(int length, bool useNum, bool useLow, bool useUpp, bool useSpe, string custom)
    {
        string s = null, str = custom;
        if (useNum == true) { str += "0123456789"; }
        if (useLow == true) { str += "abcdefghijklmnopqrstuvwxyz"; }
        if (useUpp == true) { str += "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; }
        if (useSpe == true) { str += "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"; }
        for (int i = 0; i < length; i++)
        {
            s += str.Substring(rand.Next(0, str.Length - 1), 1);
        }
        return s;
    }

    //���������
    static int GetRandomByGuid(int lenth)
    {
        if (lenth <= 0)
            return 0;
        return rand.Next(0, lenth);
    }

    public static void DebugLog(int level, string content, params object[] args)
    {
        logStringBuffer.Length = 0;
        if (content.Contains("LuaLog"))
        {
            logStringBuffer.Append(content);
            for (int i = 0; i < args.Length; ++i)
            {
                logStringBuffer.Append(" ");
                logStringBuffer.Append(args[i].ToString());
            }
            content = logStringBuffer.ToString();
            logStringBuffer.Length = 0;
        }
        else
        {
            if (args.Length != 0)
            {
                logStringBuffer.Length = 0;
                logStringBuffer.AppendFormat(content, args);
                content = logStringBuffer.ToString();
                logStringBuffer.Length = 0;
            }
        }

        WriteLog(m_debug_log_agent, level, content);
    }

    private static void WriteConsole(int level, string content)
    {
        if (!m_out_to_console)
        {
            return;
        }

        //Debug.Log(content);
#if !UNITY_EDITOR
        return;
#endif

        if (level == (int)LogManagerInterface.LogLevel.LL_WARNING) Debug.LogWarning(content);
        else if (level == (int)LogManagerInterface.LogLevel.LL_ERROR) Debug.LogError(content);
#if UNITY_EDITOR || DEV_BRANCH
        else if (level == (int)LogManagerInterface.LogLevel.LL_INFO)
            Debug.Log(content);
        else Debug.Log(content);
#endif
    }

    private static void WriteLog(LogAgent agent, int level, string content)
    {
        if (!m_out_to_file)
        {
            return;
        }

        if (level > m_record_level)
        {
            return;
        }
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

#if UNITY_EDITOR

        //��⵽����·��
        if (m_log_isPathChinese)
        {
            if(m_game_log_path_streamWriter_editor!=null)
            {
                m_game_log_path_streamWriter_editor.WriteLine(content);
                m_game_log_path_streamWriter_editor.Flush();
            }
            return;
       }
#endif

#if UNITY_IPHONE
   if(m_log_isConfusion)
    {
        //��������
        content=Confusion(content);
    }     
#endif
        //  ios
        if (m_log_by_stream_write)
        {
            if (string.IsNullOrEmpty(m_game_log_path))
            {
                if (string.IsNullOrEmpty(m_log_path)) return;
                //m_game_log_path = Path.Combine(m_log_path, "x3dgame.log");
                m_game_log_path = Path.Combine(m_log_path, m_log_name);
                m_game_log_path = m_game_log_path.Replace('\\', '/');
                // if (File.Exists(m_game_log_path))
                // {
                //     try
                //     {
                //         File.Delete(m_game_log_path);
                //     }
                //     catch (Exception e)
                //     {
                //         QLog.LogError(" delete file failed  {0} ", e);
                //     }

                // }
                m_m_game_log_path_fileStream = new FileStream(m_game_log_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                m_m_game_log_path_streamWriter = new StreamWriter(m_m_game_log_path_fileStream);
            }

            if (m_m_game_log_path_streamWriter != null)
            {
                m_m_game_log_path_streamWriter.WriteLine(content);
                m_m_game_log_path_streamWriter.Flush();
            }
        }

        //��ios
        if (m_log_by_agent && agent != null)
        {
            string current_write_buffer = string.Empty;
            while (content.Length >= m_max_str_len)
            {
                current_write_buffer = content.Substring(0, m_max_str_len);
                content = content.Substring(m_max_str_len);
                agent.print(level, current_write_buffer);
            }
            if (content.Length > 0)
                agent.print(level, content);
        }
    }

    public static void Log(string content, params object[] args)
    {
        QLog.EngineLog((int)LogManagerInterface.LogLevel.LL_INFO, content, args);
    }
    public static void IfLog(string content, params object[] args)
    {
        if (debugUILog)
        {
            QLog.Log(content, args);
        }
    }

    public static void LogWarning(string content, params object[] args)
    {
        QLog.EngineLog((int)LogManagerInterface.LogLevel.LL_WARNING, content, args);
    }

    public static void LogError(string content, params object[] args)
    {
        QLog.EngineLog((int)LogManagerInterface.LogLevel.LL_ERROR, content, args);
    }

    public static void LogDebug(string content, params object[] args)
    {
        if (!getOpenLogDebug())
            return;
        QLog.DebugLog((int)LogManagerInterface.LogLevel.LL_DEBUG, content, args);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogEditor(string content, params object[] args)
    {
        QLog.Log(content, args);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogWarningEditor(string content, params object[] args)
    {
        QLog.LogWarning(content, args);
    }

    public static void LogWarningOrError(string content, params object[] args)
    {
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR)
        LogError(content, args);    //Editor��win��ǿ�ƿ���
#else
        if (m_up_bug_log_level)
        {
            LogError(content, args);
        }
        else
        {
            LogWarning(content, args);
        }
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogTest(string content, params object[] args)
    {
        content = "TestTag: " + content;
        QLog.LogWarning(content, args);
    }


    [DllImport("kernel32.dll")]
    private static extern IntPtr _lopen(string lpPathName, int iReadWrite);
    //[DllImport("kernel32.dll")]
    //private static extern IntPtr _lopen(string lpPathName, int iReadWrite);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
    //[DllImport("kernel32.dll")]
    //private static extern bool CloseHandle(IntPtr hObject);

    private const int OF_READWRITE = 2;
    private const int OF_SHARE_DENY_NONE = 0x40;
    private static readonly IntPtr HFILE_ERROR = new IntPtr(-1);
    //private const int OF_READWRITE = 2;
    //private const int OF_SHARE_DENY_NONE = 0x40;
    //private static readonly IntPtr HFILE_ERROR = new IntPtr(-1);

    /// <summary>
    /// �ж��ļ�״̬��1���ļ��򿪣� 0���ļ��رգ� -1���ļ�������
    /// </summary>
    /// <param name="fileFullName"></param>
    /// <returns></returns>
    public static int FileStatus(string fileFullName)
    {
        if (!File.Exists(fileFullName))
        {
            return -1;
        }
        ///// <summary>
        ///// �ж��ļ�״̬��1���ļ��򿪣� 0���ļ��رգ� -1���ļ�������
        ///// </summary>
        ///// <param name="fileFullName"></param>
        ///// <returns></returns>
        //public static int FileStatus(string fileFullName)
        //{
        //    if (!File.Exists(fileFullName))
        //    {
        //        return -1;
        //    }

        IntPtr handle = _lopen(fileFullName, OF_READWRITE | OF_SHARE_DENY_NONE);
        //    IntPtr handle = _lopen(fileFullName, OF_READWRITE | OF_SHARE_DENY_NONE);

        if (handle == HFILE_ERROR)
        {
            return 1;
        }
        //    if (handle == HFILE_ERROR)
        //    {
        //        return 1;
        //    }

        CloseHandle(handle);
        //    CloseHandle(handle);

        return 0;
    }
    //    return 0;
    //}

}