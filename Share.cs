using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static L1MapViewer.Other.Struct;

namespace L1MapViewer {
    class Share {
        //共享的天堂資料夾路徑
        public static string LineagePath { get; set; } = string.Empty;

        //共享的idx資料清單
        public static Dictionary<string, Dictionary<string, L1Idx>> IdxDataList = new Dictionary<string, Dictionary<string, L1Idx>>();
        //共享的地圖資料清單
        public static Dictionary<string, L1Map> MapDataList = new Dictionary<string, L1Map>();
        //共享的地圖座標清單
        public static Dictionary<Region, Dictionary<Region, LinLocation>> RegionList = new Dictionary<Region, Dictionary<Region, LinLocation>>();
        //共享的地圖座標清單
        public static Dictionary<Region, Dictionary<Region, LinLocation>> RegionList2 = new Dictionary<Region, Dictionary<Region, LinLocation>>();
        //共享的地圖座標清單
        public static Dictionary<string, LinLocation> LinLocList = new Dictionary<string, LinLocation>();
        //共享的地圖座標清單
        public static Dictionary<string, LinLocation> LinLocList2 = new Dictionary<string, LinLocation>();

        //共享的Npc資料清單
        public static Dictionary<string, DataRow> NpcList = new Dictionary<string, DataRow>();
        //共享的Item資料清單
        public static Dictionary<string, DataRow> ItemList = new Dictionary<string, DataRow>();

        //zone3desc-c.tbl
        public static List<string> Zone3descList = new List<string>();

        //zone3-c.xml/zone3-c.tbl
        public static Dictionary<string, L1Zone> ZoneList = new Dictionary<string, L1Zone>();
    }
}
