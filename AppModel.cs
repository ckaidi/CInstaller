using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CInstaller
{
    public class AppModel
    {
        /// <summary>
        /// 程序名称
        /// </summary>
        public string EN { get; set; } = "SDSyncApp";
        public string URL { get; set; } = "http://127.0.0.1:5500/";

        /// <summary>
        /// 启动程序路径
        /// </summary>
        public string StartUpApp { get; set; } = "Clash.for.Windows.Setup.0.20.39.exe";
    }
}
