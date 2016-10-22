﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;
using Office = Microsoft.Office.Core;
using vsto = Microsoft.Office.Tools;
using System.Drawing;
using System.ComponentModel;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;

namespace ClassPieAddin
{
    public partial class ThisAddIn : IDisposable
    {
        public Timer timer = new Timer();
        public IDanmakuEngine danmakuEngine = new DanmakuEngine();

        private BackgroundWorker fetchBW = new BackgroundWorker();
        private Timer getWebContentTimer = new Timer();
        private delegate void DispatcherDelegateTimer();
        private static List<string> danmuStorage = new List<string>();
        private System.Windows.Forms.Screen[] sc = System.Windows.Forms.Screen.AllScreens;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            //danmakuEngine.ShowDanmaku("This is a long word.", System.Drawing.Color.Black, new Font("微软雅黑", 16));
            fetchBW.WorkerReportsProgress = true;
            fetchBW.WorkerSupportsCancellation = true;
            fetchBW.DoWork += new DoWorkEventHandler(FetchBW_DoWork);
            fetchBW.ProgressChanged += new ProgressChangedEventHandler(FetchBW_ProgressChanged);
            fetchBW.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FetchBW_RunWorkerCompleted);

            // 设置各计时器的属性
            timer = new System.Timers.Timer();
            timer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            timer.Interval = 2000;

            getWebContentTimer = new Timer();
            getWebContentTimer.Elapsed += new ElapsedEventHandler(getWebContentTimeOut);
            getWebContentTimer.Interval = 3000;
            getWebContentTimer.AutoReset = false; // 不会自动重置计时器，即只计时一次
            this.Application.SlideShowBegin += Application_SlideShowBegin;
            this.Application.SlideShowEnd += Application_SlideShowEnd;
        }

        private void Application_SlideShowBegin(PowerPoint.SlideShowWindow Wn) {
            timer.Start();
            if(danmakuEngine.Hidden == true) {
                danmakuEngine.Hidden = false;
            }
        }

        private void Application_SlideShowEnd(PowerPoint.Presentation Pres) {
            timer.Stop();
        }


        private void OnTimedEvent(object sender, EventArgs e) {
            if (danmuStorage.Count > 0) {
                var text = danmuStorage[0];
                danmuStorage.RemoveAt(0);
                if (danmakuEngine.DanmakuCount < Setting.num)
                    danmakuEngine.ShowDanmaku(text, Color.Black, new Font("微软雅黑",Setting.fontSize));
            }
            if (danmuStorage.Count < Setting.num) {
                if (fetchBW.IsBusy == false) {
                    getWebContentTimer.Start();
                    fetchBW.RunWorkerAsync();
                }
            }
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
        }

        protected override Microsoft.Office.Core.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
           return new MainRibbon();
        }

        #region BackgroundWorker

        /// <summary>
        /// DoWrok开始从后台获取弹幕内容
        /// 将获取到的内容保存至e.Result中
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FetchBW_DoWork(Object sender, DoWorkEventArgs e) {
            BackgroundWorker backgroundWorker = sender as BackgroundWorker;
            string textFetched = GetWebContent("http://www.zhengzi.me/danmu/olds/controller/desktop.php?func=getSeq&user=ketangpie&hashPass=9e6698f2e99b56dbb21164101f600ac0");
            int num;
            List<string> contentList = new List<string>();

            if (textFetched == "网络未连接。") {
                num = 1;
                contentList.Add(textFetched);
                // 如果网络未链接，则在弹幕中提醒
            }
            else {
                num = 0;
                try {
                    Debug.WriteLine("Fetch:" + textFetched);
                    // 将获取到的JSON进行解析得到获取的弹幕数量以及所有弹幕
                    JObject parseResult = JObject.Parse(textFetched);
                    dynamic dy1 = parseResult as dynamic;
                    num = (int)dy1["seqNum"];
                    JArray dataArray = ((JArray)dy1["seqData"]);
                    if (dataArray != null) {
                        JToken data = dataArray.First;
                        while (data != null) {
                            contentList.Add(data.ToString());
                            data = data.Next;
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonReaderException error) {
                    // 处理JSON解析错误，这里Debug提示后直接舍弃
                    Debug.WriteLine("Error: JsonReaderException");
                    Debug.WriteLine("Fetch Text: " + textFetched);
                }
            }
            // 将解析结果保存至e.Result中供RunWorkerCompleted使用
            Debug.WriteLine("Get " + contentList.Count.ToString() + " Results.");
            e.Result = contentList;
            backgroundWorker.ReportProgress(100); // 当Dowork完成时直接将进度设为100%，执行RunWorkerCompleted
        }

        private void FetchBW_ProgressChanged(object sender, ProgressChangedEventArgs e) {
            return;
        }

        /// <summary>
        /// DoWork完成时，将获取到的弹幕保存至fetchedData供UpdateText使用
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FetchBW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
            if (e.Cancelled == false && e.Error == null) {
                List<string> contentList = e.Result as List<string>;
                danmuStorage.AddRange(contentList);
                contentList.Clear();
            }
            else {
                Debug.WriteLine("获取时出现错误");
            }
            getWebContentTimer.Stop();
        }

        /// <summary>
        /// 获取网页信息，当无法连接到网络时提示
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private string GetWebContent(string url) {
            try {
                using (WebClient client = new WebClient()) {
                    byte[] buffer = client.DownloadData(url);
                    string str = Encoding.GetEncoding("UTF-8").GetString(buffer, 0, buffer.Length);
                    return str;
                }
            }
            catch (System.Net.WebException) {
                return "网络未连接。";
            }
        }

        /// <summary>
        /// 当获取超时时终止后台进程
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void getWebContentTimeOut(object sender, EventArgs e) {
            fetchBW.CancelAsync();
            Debug.WriteLine("Time Out When Get Web Content.");
        }

        #endregion

        #region VSTO 生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }
        
        #endregion
    }
}
