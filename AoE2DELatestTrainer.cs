using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Age of Empires II Definitive Edition Plus 11 Trainer")]
[assembly: AssemblyDescription("Single-player trainer for AoE2DE 101.103.54800.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Native
{
    internal const uint Q=0x400,R=0x10,W=0x20,O=0x08,T=0x02;
    internal const uint MEM_COMMIT=0x1000;
    internal const uint PAGE_GUARD=0x100,PAGE_NOACCESS=0x01;
    [StructLayout(LayoutKind.Sequential)] internal struct MBI { public IntPtr BaseAddress,AllocationBase; public uint AllocationProtect,Alignment1; public UIntPtr RegionSize; public uint State,Protect,Type,Alignment2; }
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern IntPtr OpenProcess(uint a,bool i,int p);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[]b,UIntPtr s,out UIntPtr n);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr h,IntPtr a,byte[]b,UIntPtr s,out UIntPtr n);
    [DllImport("kernel32.dll")] internal static extern UIntPtr VirtualQueryEx(IntPtr h,IntPtr a,out MBI m,UIntPtr n);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
}

internal sealed class GameContext : IDisposable
{
    internal Process Game; internal IntPtr Handle; internal ulong ModuleBase,Player,Resources; internal int PlayerId;
    public void Dispose(){if(Handle!=IntPtr.Zero){Native.CloseHandle(Handle);Handle=IntPtr.Zero;}}
}

internal sealed class GameObjectInfo
{
    internal ulong Address,Owner,RawHealth; internal int PlayerId; internal bool Unit; internal float Health;
}

internal sealed class ObjectMemoryRegion
{
    internal ulong Base,Size;
}

internal static class Engine
{
    const ulong K1=0x6AE24D3352F04511UL,K2=0xC22D6B496D833977UL,K3=0x042AC42976FCCEF5UL,K4=0xA9834B738A7DD167UL;
    const ulong HX=0x0F2A55F7A61DD00EUL,HS=0x147B5E081EC087D9UL;
    internal const ulong UnitVtableRva=0x32B8270UL,BuildingVtableRva=0x32B5FC8UL;
    const int Chunk=4*1024*1024;
    static readonly object CacheLock=new object();
    static int cachedPid;
    static ulong cachedModule,cachedGlobal,cachedPlayer,cachedResources;
    static int cachedPlayerId;
    static List<GameObjectInfo> cachedObjects=new List<GameObjectInfo>();
    static List<ObjectMemoryRegion> cachedObjectRegions=new List<ObjectMemoryRegion>();
    static DateTime objectCacheTime=DateTime.MinValue;

    internal static GameContext Open(bool write)
    {
        Process[] ps=Process.GetProcessesByName("AoE2DE_s"); if(ps.Length!=1)throw new InvalidOperationException("请先启动一局游戏（需要且只能有一个 AoE2DE_s 进程）。");
        Process p=ps[0];if(p.MainModule.FileVersionInfo.FileVersion!="101.103.54800.0")throw new InvalidOperationException("仅支持游戏版本 101.103.54800.0。检测到："+p.MainModule.FileVersionInfo.FileVersion);
        uint access=Native.Q|Native.R|(write?Native.W|Native.O:0); IntPtr h=Native.OpenProcess(access,false,p.Id);
        if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法打开游戏进程");
        try
        {
            lock(CacheLock)
            {
                if(cachedPid==p.Id&&cachedModule!=0&&cachedPlayer!=0&&cachedResources!=0)
                {
                    int id;
                    ulong cachedRoot,currentPlayer,currentResources;
                    if(TryU64(h,cachedGlobal,out cachedRoot)&&cachedRoot>=0x10000&&TryU64(h,cachedRoot+0x28,out currentPlayer)&&currentPlayer==cachedPlayer&&TryU64(h,currentPlayer+0x98,out currentResources)&&currentResources==cachedResources&&TryI32(h,cachedPlayer+0x648,out id)&&id==cachedPlayerId)
                        return new GameContext{Game=p,Handle=h,ModuleBase=cachedModule,Player=cachedPlayer,Resources=cachedResources,PlayerId=cachedPlayerId};
                }
            }
            ulong mb=(ulong)p.MainModule.BaseAddress.ToInt64(),global=0;
            lock(CacheLock)if(cachedPid==p.Id&&cachedModule==mb)global=cachedGlobal;
            if(global==0)
            {
                byte[] image=Read(h,mb,p.MainModule.ModuleMemorySize);int hit=FindRootAob(image);int mov=hit+2,disp=BitConverter.ToInt32(image,mov+3);global=(ulong)((long)mb+mov+7+disp);
                lock(CacheLock){cachedPid=p.Id;cachedModule=mb;cachedGlobal=global;cachedPlayer=0;cachedResources=0;cachedObjects.Clear();cachedObjectRegions.Clear();objectCacheTime=DateTime.MinValue;}
            }
            ulong root=U64(h,global),player=U64(h,root+0x28),resources=U64(h,player+0x98); int playerId=I32(h,player+0x648);
            if(root<0x10000||player<0x10000||resources<0x10000||playerId<1||playerId>8)throw new InvalidOperationException("当前不在有效的单人对局中。");
            lock(CacheLock){bool changed=cachedPlayer!=player;cachedPid=p.Id;cachedModule=mb;cachedGlobal=global;cachedPlayer=player;cachedResources=resources;cachedPlayerId=playerId;if(changed){cachedObjects.Clear();cachedObjectRegions.Clear();objectCacheTime=DateTime.MinValue;}}
            return new GameContext{Game=p,Handle=h,ModuleBase=mb,Player=player,Resources=resources,PlayerId=playerId};
        }
        catch{Native.CloseHandle(h);throw;}
    }

    static int FindRootAob(byte[] b)
    {
        int found=-1,count=0; for(int i=0;i<=b.Length-17;i++)if(b[i]==0xFF&&b[i+2]==0x48&&b[i+3]==0x89&&b[i+4]==0x35&&b[i+9]==0x48&&(b[i+10]&0xF0)==0x80&&b[i+14]==0&&b[i+15]==0&&b[i+16]==0xE8){found=i;count++;}
        if(count!=1)throw new InvalidOperationException("当前版本的动态根特征不唯一（命中 "+count+"）。"); return found;
    }

    internal static float Resource(GameContext c,int id){return DecodeResource(U64(c.Handle,c.Resources+(ulong)id*8));}
    internal static void SetResource(GameContext c,int id,float value){Write(c.Handle,c.Resources+(ulong)id*8,BitConverter.GetBytes(EncodeResource(value)));}
    internal static void SetResource(int id,float value){using(GameContext c=Open(true))SetResource(c,id,value);}
    internal static void SetFourResources(float value){using(GameContext c=Open(true)){SetResource(c,0,value);SetResource(c,1,value);SetResource(c,2,value);SetResource(c,3,value);}}

    internal static List<GameObjectInfo> ScanObjects(GameContext c)
    {
        ulong unit=c.ModuleBase+UnitVtableRva,building=c.ModuleBase+BuildingVtableRva; var result=new List<GameObjectInfo>();var objectRegions=new List<ObjectMemoryRegion>();ulong cursor=0x10000; int mbiSize=Marshal.SizeOf(typeof(Native.MBI));
        while(cursor<0x00007FFFFFFF0000UL)
        {
            Native.MBI m; if(Native.VirtualQueryEx(c.Handle,new IntPtr((long)cursor),out m,new UIntPtr((uint)mbiSize))==UIntPtr.Zero)break; ulong rb=(ulong)m.BaseAddress.ToInt64(),rs=m.RegionSize.ToUInt64(),next=rb+rs;if(next<=cursor)break;
            if(m.State==Native.MEM_COMMIT&&(m.Protect&(Native.PAGE_GUARD|Native.PAGE_NOACCESS))==0)
            {int before=result.Count;ScanRegion(c,rb,rs,unit,building,result);if(result.Count>before)objectRegions.Add(new ObjectMemoryRegion{Base=rb,Size=rs});}
            cursor=next;
        }
        lock(CacheLock)cachedObjectRegions=objectRegions;
        return result;
    }

    static void ScanRegion(GameContext c,ulong rb,ulong rs,ulong unit,ulong building,List<GameObjectInfo> result)
    {
        for(ulong done=0;done<rs;)
        {
            int want=(int)Math.Min((ulong)Chunk,rs-done);byte[] buf=new byte[want];UIntPtr got;
            if(Native.ReadProcessMemory(c.Handle,new IntPtr((long)(rb+done)),buf,new UIntPtr((uint)want),out got)&&got.ToUInt64()>=8)
            for(int i=0;i<=(int)got.ToUInt64()-8;i+=8)
            {
                ulong vt=BitConverter.ToUInt64(buf,i);if(vt!=unit&&vt!=building)continue;ulong obj=rb+done+(ulong)i,owner,raw;if(!TryU64(c.Handle,obj+0x18,out owner)||owner<0x10000||!TryU64(c.Handle,obj+0x130,out raw))continue;int pid;if(!TryI32(c.Handle,owner+0x648,out pid))continue;float hp=DecodeHealth(raw);if(float.IsNaN(hp)||float.IsInfinity(hp)||hp<0||hp>100000)continue;
                result.Add(new GameObjectInfo{Address=obj,Owner=owner,RawHealth=raw,PlayerId=pid,Unit=vt==unit,Health=hp});
            }
            done+=(ulong)want;
        }
    }

    static List<GameObjectInfo> ScanObjectsFast(GameContext c)
    {
        List<ObjectMemoryRegion> regions;lock(CacheLock)regions=new List<ObjectMemoryRegion>(cachedObjectRegions);if(regions.Count==0)return ScanObjects(c);
        ulong unit=c.ModuleBase+UnitVtableRva,building=c.ModuleBase+BuildingVtableRva;var result=new List<GameObjectInfo>();foreach(ObjectMemoryRegion r in regions)ScanRegion(c,r.Base,r.Size,unit,building,result);
        lock(CacheLock){cachedObjects=result;objectCacheTime=DateTime.UtcNow;}return result;
    }

    internal static int RefreshObjectCache()
    {
        using(GameContext c=Open(false))
        {
            List<GameObjectInfo> objects=ScanObjects(c);
            lock(CacheLock){if(c.Game.Id==cachedPid&&c.Player==cachedPlayer){cachedObjects=objects;objectCacheTime=DateTime.UtcNow;}}
            return objects.Count;
        }
    }

    static List<GameObjectInfo> Objects(GameContext c)
    {
        lock(CacheLock)if(c.Game.Id==cachedPid&&c.Player==cachedPlayer&&cachedObjects.Count>0&&DateTime.UtcNow-objectCacheTime<TimeSpan.FromSeconds(90))return new List<GameObjectInfo>(cachedObjects);
        List<GameObjectInfo> objects=ScanObjects(c);lock(CacheLock){cachedObjects=objects;objectCacheTime=DateTime.UtcNow;}return objects;
    }

    internal static string RestoreHealth()
    {
        using(GameContext c=Open(true)){int u=0,b=0;foreach(GameObjectInfo o in ScanObjectsFast(c)){if(o.Owner!=c.Player)continue;try{ulong raw=U64(c.Handle,o.Address+0x130);float v=o.Unit?999f:9999f;Write(c.Handle,o.Address+0x130,BitConverter.GetBytes(EncodeHealth(raw,v)));if(o.Unit)u++;else b++;}catch{}}return string.Format("已恢复：{0} 个单位、{1} 栋建筑",u,b);}
    }
    internal static string EmptyAiHealth()
    {
        using(GameContext c=Open(true)){int u=0,b=0;foreach(GameObjectInfo o in ScanObjectsFast(c)){if(o.PlayerId<1||o.PlayerId>8||o.Owner==c.Player||o.Health<=0)continue;try{ulong raw=U64(c.Handle,o.Address+0x130);Write(c.Handle,o.Address+0x130,BitConverter.GetBytes(EncodeHealth(raw,1f)));if(o.Unit)u++;else b++;}catch{}}return string.Format("AI 已设为 1 血：{0} 个单位、{1} 栋建筑",u,b);}
    }
    internal static int FinishQueues(GameContext c,List<ulong> buildings,out int active)
    {
        int n=0;active=0;foreach(ulong building in buildings){ulong queue,node,action;if(!TryU64(c.Handle,building+0x8A8,out queue)||queue==0||!TryU64(c.Handle,queue+0x10,out node)||node==0||!TryU64(c.Handle,node,out action)||action==0)continue;uint type;if(!TryU32(c.Handle,action+8,out type)||(type&0xFFFF)!=0x66)continue;active++;uint raw;if(!TryU32(c.Handle,action+0x6C,out raw))continue;float elapsed=BitConverter.ToSingle(BitConverter.GetBytes(raw),0);if(float.IsNaN(elapsed)||elapsed>=1000f)continue;Write(c.Handle,action+0x6C,BitConverter.GetBytes(100000f));n++;}return n;
    }
    internal static List<ulong> PlayerBuildings(GameContext c){var x=new List<ulong>();foreach(GameObjectInfo o in ScanObjectsFast(c))if(o.Owner==c.Player&&!o.Unit)x.Add(o.Address);return x;}
    internal static string InstantRecruitOnce()
    {
        using(GameContext c=Open(true))
        {
            List<ulong> buildings=PlayerBuildings(c);int total=0,idle=0;DateTime end=DateTime.UtcNow.AddMinutes(2);
            while(DateTime.UtcNow<end&&idle<8){int active;total+=FinishQueues(c,buildings,out active);if(active==0)idle++;else idle=0;Thread.Sleep(25);}
            return total==0?"当前没有生产队列":"当前生产队列已全部完成（处理 "+total+" 项）";
        }
    }
    internal static string InstantResearchOnce()
    {
        using(GameContext c=Open(true))
        {
            SetResource(c,86,0.001f);try{Thread.Sleep(1500);return "当前研发队列已执行";}finally{SetResource(c,86,1f);}
        }
    }
    internal static string InstantBuildingsOnce()
    {
        using(GameContext c=Open(true))
        {
            SetResource(c,195,1000f);try{Thread.Sleep(1500);return "当前建造任务已执行";}finally{SetResource(c,195,1f);}
        }
    }

    static float DecodeResource(ulong raw){ulong v=((raw^K1)^K2)-K3;v^=K4;return BitConverter.ToSingle(BitConverter.GetBytes((uint)v),0);}
    static ulong EncodeResource(float v){ulong bits=BitConverter.ToUInt32(BitConverter.GetBytes(v),0);return (((bits^K4)+K3)^K2)^K1;}
    static float DecodeHealth(ulong raw){ulong p=unchecked((raw^HX)-HS);return BitConverter.ToSingle(BitConverter.GetBytes((uint)p),0);}
    static ulong EncodeHealth(ulong raw,float v){ulong p=unchecked((raw^HX)-HS),bits=BitConverter.ToUInt32(BitConverter.GetBytes(v),0);p=(p&0xFFFFFFFF00000000UL)|bits;return unchecked((p+HS)^HX);}
    internal static byte[] Read(IntPtr h,ulong a,int n){byte[]b=new byte[n];UIntPtr g;if(!Native.ReadProcessMemory(h,new IntPtr((long)a),b,new UIntPtr((uint)n),out g)||g.ToUInt64()!=(ulong)n)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"读取游戏内存失败");return b;}
    internal static void Write(IntPtr h,ulong a,byte[]b){UIntPtr n;if(!Native.WriteProcessMemory(h,new IntPtr((long)a),b,new UIntPtr((uint)b.Length),out n)||n.ToUInt64()!=(ulong)b.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"写入游戏内存失败");}
    static ulong U64(IntPtr h,ulong a){return BitConverter.ToUInt64(Read(h,a,8),0);} static int I32(IntPtr h,ulong a){return BitConverter.ToInt32(Read(h,a,4),0);}
    static bool TryU64(IntPtr h,ulong a,out ulong v){v=0;byte[]b=new byte[8];UIntPtr n;if(!Native.ReadProcessMemory(h,new IntPtr((long)a),b,new UIntPtr(8),out n)||n.ToUInt64()!=8)return false;v=BitConverter.ToUInt64(b,0);return true;}
    static bool TryU32(IntPtr h,ulong a,out uint v){v=0;byte[]b=new byte[4];UIntPtr n;if(!Native.ReadProcessMemory(h,new IntPtr((long)a),b,new UIntPtr(4),out n)||n.ToUInt64()!=4)return false;v=BitConverter.ToUInt32(b,0);return true;}
    static bool TryI32(IntPtr h,ulong a,out int v){uint x;if(!TryU32(h,a,out x)){v=0;return false;}v=(int)x;return true;}
}

internal sealed class TrainerForm : Form
{
    readonly Color Dark=Color.FromArgb(30,32,34),PanelColor=Color.FromArgb(35,37,39),Orange=Color.FromArgb(255,140,0);
    readonly Label processInfo=new Label(),status=new Label();
    readonly NumericUpDown wood=ResourceBox(),food=ResourceBox(),gold=ResourceBox(),stone=ResourceBox();
    readonly System.Windows.Forms.Timer detector=new System.Windows.Forms.Timer();
    int hookedPid,detectedPid,language;bool hookBusy;DateTime nextCacheRefresh=DateTime.MinValue,nextAttachAttempt=DateTime.MinValue;
    const int WM_HOTKEY=0x0312;

    internal TrainerForm()
    {
        Text="Age of Empires II: Definitive Edition +11 Trainer";ClientSize=new Size(790,650);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Dark;ForeColor=Color.Gainsboro;Font=new Font("Microsoft YaHei UI",9.5f);
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AoE.Icon"))if(stream!=null)Icon=new Icon(stream);
        BuildUi();detector.Interval=1000;detector.Tick+=delegate{DetectGame();};Shown+=delegate{RegisterKeys();detector.Start();DetectGame();};
    }

    string T(string simplified,string traditional,string english){return language==0?simplified:(language==1?traditional:english);}
    void BuildUi()
    {
        Controls.Clear();
        var header=new Panel{Dock=DockStyle.Top,Height=70,BackColor=Color.FromArgb(28,30,31)};Controls.Add(header);
        header.Controls.Add(new Label{Text="Age of Empires II: Definitive Edition",ForeColor=Orange,Font=new Font(Font.FontFamily,15,FontStyle.Bold),AutoSize=true,Location=new Point(68,12)});
        header.Controls.Add(new Label{Text="v101.103.54800.0 - Build 185872  Plus 11 Trainer",ForeColor=Color.Gainsboro,Font=new Font(Font.FontFamily,10,FontStyle.Bold),AutoSize=true,Location=new Point(94,39)});
        AddLanguage(header,"简体",0,570);AddLanguage(header,"繁體",1,625);AddLanguage(header,"English",2,680);

        var left=new Panel{Location=new Point(6,74),Size=new Size(220,568),BackColor=PanelColor};Controls.Add(left);
        var cover=new PictureBox{Location=new Point(0,0),Size=new Size(220,310),BackColor=Color.FromArgb(44,48,50),BorderStyle=BorderStyle.FixedSingle,SizeMode=PictureBoxSizeMode.StretchImage};left.Controls.Add(cover);
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AoE.Cover"))if(stream!=null)using(Image image=Image.FromStream(stream))cover.Image=new Bitmap(image);
        processInfo.Location=new Point(4,322);processInfo.Size=new Size(212,150);processInfo.ForeColor=Color.Silver;left.Controls.Add(processInfo);
        left.Controls.Add(new Label{Text=T("适配 Build 185872\r\n仅限单机使用","適配 Build 185872\r\n僅限單機使用","Adapted for Build 185872\r\nSingle-player use only"),Location=new Point(4,500),Size=new Size(210,45),ForeColor=Color.Gray});
        var projectLink=new LinkLabel{Text=T("项目主页：GitHub","專案首頁：GitHub","Project page: GitHub"),Location=new Point(4,545),AutoSize=true,LinkColor=Orange,ActiveLinkColor=Color.White,Font=new Font(Font.FontFamily,8.5f,FontStyle.Underline)};
        projectLink.LinkClicked+=delegate{try{Process.Start(new ProcessStartInfo("https://github.com/KagurazakaNatsuki/AOE2-Trainer"){UseShellExecute=true});}catch(Exception e){SetStatus(T("无法打开项目主页：","無法開啟專案首頁：","Cannot open project page: ")+e.Message,true);}};
        left.Controls.Add(projectLink);

        var right=new Panel{Location=new Point(232,74),Size=new Size(552,568),BackColor=Dark};Controls.Add(right);
        right.Controls.Add(new Label{Text=T("热键","熱鍵","Hotkeys"),Font=new Font(Font.FontFamily,11,FontStyle.Bold),Location=new Point(4,4),AutoSize=true});
        right.Controls.Add(new Label{Text=T("选项","選項","Options"),Font=new Font(Font.FontFamily,11,FontStyle.Bold),Location=new Point(160,4),AutoSize=true});
        right.Controls.Add(new Label{BackColor=Color.Gray,Location=new Point(0,29),Size=new Size(542,1)});
        right.Controls.Add(new Label{Text=T("特别说明：使用修改器前请先进入单人战局！","特別說明：使用修改器前請先進入單人戰局！","Special Notes: Enter a single-player match before use!"),ForeColor=Orange,Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold),Location=new Point(5,44),AutoSize=true});
        int y=78;
        AddRow(right,ref y,"Num 1",T("修改木材","修改木材","Edit Wood"),wood,delegate{SetResource(1,T("木材","木材","Wood"),wood);});
        AddRow(right,ref y,"Num 2",T("修改食物","修改食物","Edit Food"),food,delegate{SetResource(0,T("食物","食物","Food"),food);});
        AddRow(right,ref y,"Num 3",T("修改黄金","修改黃金","Edit Gold"),gold,delegate{SetResource(3,T("黄金","黃金","Gold"),gold);});
        AddRow(right,ref y,"Num 4",T("修改石料","修改石料","Edit Stone"),stone,delegate{SetResource(2,T("石料","石料","Stone"),stone);});
        AddRow(right,ref y,"Num 6",T("恢复单位与建筑生命值","恢復單位與建築生命值","Restore Units & Buildings Health"),null,delegate{Run(T("恢复生命值","恢復生命值","Restore Health"),delegate{Engine.RestoreHealth();return T("生命值恢复完成","生命值恢復完成","Health restored");});});
        AddRow(right,ref y,"Num 7",T("瞬间研发（一次）","瞬間研發（一次）","Instant Research (Once)"),null,delegate{Run(T("瞬间研发","瞬間研發","Instant Research"),delegate{Engine.InstantResearchOnce();return T("当前研发队列已完成","當前研發佇列已完成","Current research queue completed");});});
        AddRow(right,ref y,"Num 8",T("瞬间建造（一次）","瞬間建造（一次）","Instant Buildings (Once)"),null,delegate{Run(T("瞬间建造","瞬間建造","Instant Buildings"),delegate{Engine.InstantBuildingsOnce();return T("当前建造任务已完成","當前建造任務已完成","Current construction completed");});});
        AddRow(right,ref y,"Num 9",T("瞬间生产单位（一次）","瞬間生產單位（一次）","Instant Recruit Units (Once)"),null,delegate{Run(T("瞬间生产单位","瞬間生產單位","Instant Recruit Units"),delegate{Engine.InstantRecruitOnce();return T("当前生产队列已完成","當前生產佇列已完成","Current production queue completed");});});
        AddRow(right,ref y,"Num 0",T("AI 单位与建筑设为 1 血","AI 單位與建築設為 1 血","Set AI Units & Buildings to 1 HP"),null,delegate{Run(T("清空 AI 生命值","清空 AI 生命值","Empty AI Health"),delegate{Engine.EmptyAiHealth();return T("AI 单位与建筑已设为 1 血","AI 單位與建築已設為 1 血","AI units and buildings set to 1 HP");});});
        AddRow(right,ref y,"Num +",T("最大人口上限 200","最大人口上限 200","Max Population Cap: 200"),null,delegate{Run(T("最大人口","最大人口","Max Population"),delegate{Engine.SetResource(4,200);return T("人口上限已设为 200","人口上限已設為 200","Population cap set to 200");});});
        AddRow(right,ref y,"Num -",T("当前人口设为 0（无限人口）","當前人口設為 0（無限人口）","Set Population To 0 (Infinite Population)"),null,delegate{Run(T("人口归零","人口歸零","Zero Population"),delegate{Engine.SetResource(11,0);return T("当前人口已设为 0","當前人口已設為 0","Current population set to 0");});});
        status.Location=new Point(0,500);status.Size=new Size(542,54);status.BorderStyle=BorderStyle.FixedSingle;status.Padding=new Padding(8);status.ForeColor=Color.Silver;right.Controls.Add(status);
        UpdateProcessInfo();if(string.IsNullOrEmpty(status.Text))status.Text=T("等待游戏进程……","等待遊戲進程……","Waiting for game process...");
    }

    void AddLanguage(Panel header,string text,int id,int x){var l=new LinkLabel{Text=(language==id?"● ":"○ ")+text,LinkColor=language==id?Orange:Color.Gainsboro,ActiveLinkColor=Orange,AutoSize=true,Location=new Point(x,25),Font=new Font(Font.FontFamily,9,language==id?FontStyle.Bold:FontStyle.Regular)};l.LinkClicked+=delegate{language=id;BuildUi();};header.Controls.Add(l);}

    static NumericUpDown ResourceBox(){return new NumericUpDown{Minimum=0,Maximum=99999999,Value=999999,Increment=1000,Size=new Size(92,24),ForeColor=Color.Orange,BackColor=Color.FromArgb(30,32,34),BorderStyle=BorderStyle.FixedSingle,TextAlign=HorizontalAlignment.Center};}
    void AddRow(Panel p,ref int y,string hotkey,string option,NumericUpDown value,Action action)
    {
        p.Controls.Add(new Label{Text=hotkey,Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold),Location=new Point(5,y+4),Size=new Size(105,24)});
        var apply=new Button{Text=T("应用","套用","APPLY"),Location=new Point(118,y),Size=new Size(45,24),FlatStyle=FlatStyle.Flat,BackColor=Color.Gainsboro,ForeColor=Color.FromArgb(40,40,40),Font=new Font(Font.FontFamily,7.5f,FontStyle.Bold),TabStop=false};apply.FlatAppearance.BorderSize=0;apply.Click+=delegate{action();};p.Controls.Add(apply);
        p.Controls.Add(new Label{Text=option,Location=new Point(170,y+3),Size=new Size(value==null?365:245,24),Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold)});
        if(value!=null){value.Location=new Point(420,y);p.Controls.Add(value);}y+=34;
    }
    void SetResource(int id,string name,NumericUpDown box){float v=(float)box.Value;Run(T("设置","設定","Set ")+name,delegate{Engine.SetResource(id,v);return name+T("已设为 ","已設為 "," set to ")+v.ToString("0.##");});}
    void Run(string name,Func<string> work){SetStatus(name+T("处理中……","處理中……"," processing..."));Task.Run(delegate{try{string result=work();BeginInvoke((Action)delegate{SetStatus(result);});}catch(Exception e){BeginInvoke((Action)delegate{SetStatus(T("错误：","錯誤：","Error: ")+e.Message,true);});}});}
    void SetStatus(string s,bool error=false){status.Text=DateTime.Now.ToString("HH:mm:ss")+"  "+s;status.ForeColor=error?Color.Salmon:Color.LightGreen;}
    void DetectGame()
    {
        Process[] ps=Process.GetProcessesByName("AoE2DE_s");if(ps.Length!=1){hookedPid=detectedPid=0;UpdateProcessInfo();return;}int pid=ps[0].Id;detectedPid=pid;
        if(DateTime.UtcNow<nextAttachAttempt||hookedPid==pid&&DateTime.UtcNow<nextCacheRefresh){UpdateProcessInfo();return;}if(hookBusy)return;hookBusy=true;UpdateProcessInfo();
        Task.Run(delegate{try{int count=Engine.RefreshObjectCache();BeginInvoke((Action)delegate{hookedPid=pid;nextCacheRefresh=DateTime.UtcNow.AddSeconds(60);UpdateProcessInfo();SetStatus(T("已连接游戏，对象缓存 ","已連接遊戲，物件快取 ","Game hooked, object cache: ")+count);});}catch(Exception e){BeginInvoke((Action)delegate{nextAttachAttempt=DateTime.UtcNow.AddSeconds(5);UpdateProcessInfo();if(e.Message.StartsWith("仅支持游戏版本"))SetStatus(e.Message,true);});}finally{hookBusy=false;}});
    }
    void UpdateProcessInfo(){string state=detectedPid==0?T("等待游戏","等待遊戲","Waiting for game"):(hookedPid==detectedPid?T("已连接 / 就绪","已連接 / 就緒","Hooked / Ready"):T("已检测 / 等待进入战局","已偵測 / 等待進入戰局","Detected / waiting for match"));processInfo.Text=T("游戏进程名称：","遊戲程序名稱：","Game Process Name:")+"\r\nAoE2DE_s.exe\r\n\r\n"+T("进程 ID：","程序 ID：","Process ID: ")+(detectedPid==0?"--":detectedPid.ToString())+"\r\n\r\n"+T("状态：","狀態：","Status: ")+state;}
    void RegisterKeys(){int[] keys={0x61,0x62,0x63,0x64,0x66,0x67,0x68,0x69,0x60,0x6B,0x6D};bool ok=true;for(int i=0;i<keys.Length;i++)ok&=Native.RegisterHotKey(Handle,i+1,0x4000,(uint)keys[i]);if(!ok)SetStatus(T("部分热键注册失败，请关闭其他修改器。","部分熱鍵註冊失敗，請關閉其他修改器。","Some hotkeys failed; close other trainers."),true);}
    protected override void OnFormClosed(FormClosedEventArgs e){detector.Stop();for(int i=1;i<=11;i++)Native.UnregisterHotKey(Handle,i);base.OnFormClosed(e);}
    protected override void WndProc(ref Message m){if(m.Msg==WM_HOTKEY){switch(m.WParam.ToInt32()){case 1:SetResource(1,T("木材","木材","Wood"),wood);break;case 2:SetResource(0,T("食物","食物","Food"),food);break;case 3:SetResource(3,T("黄金","黃金","Gold"),gold);break;case 4:SetResource(2,T("石料","石料","Stone"),stone);break;case 5:Run(T("恢复生命值","恢復生命值","Restore Health"),delegate{Engine.RestoreHealth();return T("生命值恢复完成","生命值恢復完成","Health restored");});break;case 6:Run(T("瞬间研发","瞬間研發","Instant Research"),delegate{Engine.InstantResearchOnce();return T("当前研发队列已完成","當前研發佇列已完成","Current research queue completed");});break;case 7:Run(T("瞬间建造","瞬間建造","Instant Buildings"),delegate{Engine.InstantBuildingsOnce();return T("当前建造任务已完成","當前建造任務已完成","Current construction completed");});break;case 8:Run(T("瞬间生产单位","瞬間生產單位","Instant Recruit Units"),delegate{Engine.InstantRecruitOnce();return T("当前生产队列已完成","當前生產佇列已完成","Current production queue completed");});break;case 9:Run(T("清空 AI 生命值","清空 AI 生命值","Empty AI Health"),delegate{Engine.EmptyAiHealth();return T("AI 单位与建筑已设为 1 血","AI 單位與建築已設為 1 血","AI set to 1 HP");});break;case 10:Run(T("最大人口","最大人口","Max Population"),delegate{Engine.SetResource(4,200);return T("人口上限已设为 200","人口上限已設為 200","Population cap set to 200");});break;case 11:Run(T("人口归零","人口歸零","Zero Population"),delegate{Engine.SetResource(11,0);return T("当前人口已设为 0","當前人口已設為 0","Current population set to 0");});break;}return;}base.WndProc(ref m);}
}

internal static class Entry
{
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new TrainerForm());}
}
