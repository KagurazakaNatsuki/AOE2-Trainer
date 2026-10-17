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

[assembly: AssemblyTitle("Age of Empires II Definitive Edition Plus 12 Trainer V2.1")]
[assembly: AssemblyDescription("Single-player trainer for AoE2DE 101.103.54800.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.3")]

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
    internal const ulong UnitVtableRva=0x32B8270UL,BuildingVtableRva=0x32B5FC8UL,ResearchActionVtableRva=0x3228798UL;
    const ulong SpeedManagerOffset=0x8UL,SpeedSettingsOffset=0x228UL,SpeedValuesOffset=0xCUL;
    const ulong BuildingQueueOffset=0x8A8UL,QueueFirstNodeOffset=0x10UL,RecruitElapsedOffset=0x6CUL,ResearchIdOffset=0x68UL,ResearchElapsedOffset=0x70UL;
    const uint RecruitActionKind=0x66,ResearchActionKind=0x67;
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

    internal static float GetGameSpeed(GameContext c)
    {
        ulong address=GameSpeedAddress(c);
        byte[] pair=Read(c.Handle,address,8);
        float selected=BitConverter.ToSingle(pair,0),current=BitConverter.ToSingle(pair,4);
        if(float.IsNaN(selected)||float.IsInfinity(selected)||selected<0.1f||selected>10f||Math.Abs(selected-current)>0.01f)
            throw new InvalidOperationException("游戏速度字段未通过校验。");
        return selected;
    }

    static ulong GameSpeedAddress(GameContext c)
    {
        ulong manager=U64(c.Handle,c.Player+SpeedManagerOffset);
        if(manager<0x10000)throw new InvalidOperationException("无法定位游戏速度管理器。");
        ulong settings=U64(c.Handle,manager+SpeedSettingsOffset);
        if(settings<0x10000)throw new InvalidOperationException("无法定位游戏速度字段。");
        return settings+SpeedValuesOffset;
    }

    internal static float SetGameSpeed(float value)
    {
        using(GameContext c=Open(true))return SetGameSpeed(c,value);
    }

    internal static float SetGameSpeed(GameContext c,float value)
    {
        if(float.IsNaN(value)||float.IsInfinity(value)||value<0.5f||value>10f)throw new ArgumentOutOfRangeException("value","游戏速度必须在 0.5–10 倍之间。");
            ulong address=GameSpeedAddress(c);
            byte[] before=Read(c.Handle,address,8);
            float selected=BitConverter.ToSingle(before,0),current=BitConverter.ToSingle(before,4);
            if(float.IsNaN(selected)||float.IsInfinity(selected)||selected<0.1f||selected>10f||Math.Abs(selected-current)>0.01f)
                throw new InvalidOperationException("游戏速度字段未通过校验，未写入。");
            byte[] pair=new byte[8];byte[] bits=BitConverter.GetBytes(value);
            Buffer.BlockCopy(bits,0,pair,0,4);Buffer.BlockCopy(bits,0,pair,4,4);
            Write(c.Handle,address,pair);
            byte[] after=Read(c.Handle,address,8);
            if(Math.Abs(BitConverter.ToSingle(after,0)-value)>0.001f||Math.Abs(BitConverter.ToSingle(after,4)-value)>0.001f)
            {
                Write(c.Handle,address,before);
                throw new InvalidOperationException("游戏速度写入后校验失败，已尝试恢复原值。");
            }
            return value;
    }

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
        using(GameContext c=Open(true)){int u=0,b=0;foreach(GameObjectInfo o in ScanObjectsFast(c)){if(o.Owner!=c.Player)continue;try{ulong raw=U64(c.Handle,o.Address+0x130);Write(c.Handle,o.Address+0x130,BitConverter.GetBytes(EncodeHealth(raw,9999f)));if(o.Unit)u++;else b++;}catch{}}return string.Format("已恢复：{0} 个单位、{1} 栋建筑",u,b);}
    }
    internal static string EmptyAiHealth()
    {
        using(GameContext c=Open(true)){int u=0,b=0;foreach(GameObjectInfo o in ScanObjectsFast(c)){if(o.PlayerId<1||o.PlayerId>8||o.Owner==c.Player||o.Health<=0)continue;try{ulong raw=U64(c.Handle,o.Address+0x130);Write(c.Handle,o.Address+0x130,BitConverter.GetBytes(EncodeHealth(raw,1f)));if(o.Unit)u++;else b++;}catch{}}return string.Format("AI 已设为 1 血：{0} 个单位、{1} 栋建筑",u,b);}
    }
    internal static int FinishQueues(GameContext c,List<ulong> buildings,uint expectedKind,ulong elapsedOffset,float completionElapsed,out int active)
    {
        int written=0;active=0;
        foreach(ulong building in buildings)
        {
            ulong queue,node,action;
            if(!TryU64(c.Handle,building+BuildingQueueOffset,out queue)||queue<0x10000||
               !TryU64(c.Handle,queue+QueueFirstNodeOffset,out node)||node<0x10000||
               !TryU64(c.Handle,node,out action)||action<0x10000)continue;
            uint kind;
            if(!TryU32(c.Handle,action+8,out kind)||(kind&0xFFFF)!=expectedKind)continue;
            if(expectedKind==ResearchActionKind)
            {
                ulong vtable;uint techId;
                if(!TryU64(c.Handle,action,out vtable)||vtable!=c.ModuleBase+ResearchActionVtableRva||
                   !TryU32(c.Handle,action+ResearchIdOffset,out techId)||techId==0||techId>10000)continue;
            }
            active++;
            uint raw;ulong currentAction;
            if(!TryU32(c.Handle,action+elapsedOffset,out raw)||
               !TryU64(c.Handle,node,out currentAction)||currentAction!=action)continue;
            float elapsed=BitConverter.ToSingle(BitConverter.GetBytes(raw),0);
            if(float.IsNaN(elapsed)||float.IsInfinity(elapsed)||elapsed<0||elapsed>=1000f)continue;
            Write(c.Handle,action+elapsedOffset,BitConverter.GetBytes(completionElapsed));
            written++;
        }
        return written;
    }
    internal static List<ulong> PlayerBuildings(GameContext c){var x=new List<ulong>();foreach(GameObjectInfo o in ScanObjectsFast(c))if(o.Owner==c.Player&&!o.Unit)x.Add(o.Address);return x;}
    internal static string InstantRecruitOnce()
    {
        using(GameContext c=Open(true))
        {
            List<ulong> buildings=PlayerBuildings(c);int total=0,idle=0;DateTime end=DateTime.UtcNow.AddMinutes(2);
            while(DateTime.UtcNow<end&&idle<8){int active;total+=FinishQueues(c,buildings,RecruitActionKind,RecruitElapsedOffset,100000f,out active);if(active==0)idle++;else idle=0;Thread.Sleep(25);}
            return total==0?"当前没有生产队列":"当前生产队列已全部完成（处理 "+total+" 项）";
        }
    }
    internal static string InstantResearchOnce()
    {
        using(GameContext c=Open(true))
        {
            List<ulong> buildings=PlayerBuildings(c);
            float original=Resource(c,86);
            if(float.IsNaN(original)||float.IsInfinity(original)||original<=0||original>100)
                throw new InvalidOperationException("研发速度原值未通过校验，未修改。");
            int total=0;
            SetResource(c,86,0.001f);
            try
            {
                DateTime end=DateTime.UtcNow.AddMilliseconds(1500);
                do
                {
                    int active;
                    total+=FinishQueues(c,buildings,ResearchActionKind,ResearchElapsedOffset,1000f,out active);
                    Thread.Sleep(25);
                }while(DateTime.UtcNow<end);
                return "当前研发队列已执行（直接完成 "+total+" 项）";
            }
            finally{SetResource(c,86,original);}
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

internal sealed class SpeedToggleSwitch : Control
{
    bool isOn;
    internal bool IsOn { get { return isOn; } set { if(isOn!=value){isOn=value;Invalidate();} } }

    internal SpeedToggleSwitch()
    {
        Size=new Size(52,24);BackColor=Color.FromArgb(30,32,34);Cursor=Cursors.Hand;TabStop=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color track=isOn?Color.FromArgb(255,140,0):Color.FromArgb(17,18,19);
        using(var brush=new SolidBrush(track))
        {
            e.Graphics.FillEllipse(brush,0,1,22,22);
            e.Graphics.FillRectangle(brush,11,1,30,22);
            e.Graphics.FillEllipse(brush,30,1,22,22);
        }
        using(var brush=new SolidBrush(Color.Gainsboro))
            e.Graphics.FillEllipse(brush,isOn?30:2,3,18,18);
    }
}

internal sealed class RoundedApplyButton : Control
{
    bool hovered,pressed;

    internal RoundedApplyButton()
    {
        Size=new Size(52,24);BackColor=Color.FromArgb(30,32,34);ForeColor=Color.FromArgb(40,40,40);
        Font=new Font("Microsoft YaHei UI",7.5f,FontStyle.Bold);Cursor=Cursors.Hand;TabStop=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var rect=new Rectangle(0,0,Width-1,Height-1);
        using(var path=new GraphicsPath())
        {
            const int radius=5,diameter=radius*2;
            path.AddArc(rect.Left,rect.Top,diameter,diameter,180,90);
            path.AddArc(rect.Right-diameter,rect.Top,diameter,diameter,270,90);
            path.AddArc(rect.Right-diameter,rect.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(rect.Left,rect.Bottom-diameter,diameter,diameter,90,90);
            path.CloseFigure();
            Color fill=pressed?Color.FromArgb(173,173,173):(hovered?Color.White:Color.Gainsboro);
            using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(0,0,Width,Height),ForeColor,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
    }

    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
}

internal sealed class TrainerForm : Form
{
    readonly Color Dark=Color.FromArgb(30,32,34),PanelColor=Color.FromArgb(35,37,39),Orange=Color.FromArgb(255,140,0);
    readonly Label processInfo=new Label(),status=new Label(),hotkeyHint=new Label();
    readonly SpeedToggleSwitch speedToggle=new SpeedToggleSwitch();
    readonly TextBox speedEditor=new TextBox();
    readonly NumericUpDown wood=ResourceBox(),food=ResourceBox(),gold=ResourceBox(),stone=ResourceBox(),speed=SpeedBox();
    readonly System.Windows.Forms.Timer detector=new System.Windows.Forms.Timer();
    int hookedPid,detectedPid,language,speedPid;bool hookBusy,hotkeysEnabled=true,speedOverrideActive;ulong speedPlayer;float savedSpeed;
    Func<string> statusMessage;DateTime statusTime;bool statusError;
    DateTime nextCacheRefresh=DateTime.MinValue,nextAttachAttempt=DateTime.MinValue;
    const int WM_HOTKEY=0x0312;

    internal TrainerForm()
    {
        Text="Age of Empires II: Definitive Edition +12 Trainer V2.1";ClientSize=new Size(790,680);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Dark;ForeColor=Color.Gainsboro;Font=new Font("Microsoft YaHei UI",9.5f);
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AoE.Icon"))if(stream!=null)Icon=new Icon(stream);
        speedToggle.Click+=delegate{ToggleSpeed();};speed.ValueChanged+=delegate{speedEditor.Text=FormatSpeed();UpdateActiveSpeed();};
        speedEditor.Leave+=delegate{CommitSpeedEditor();};
        speedEditor.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){CommitSpeedEditor();e.Handled=true;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){speedEditor.Text=FormatSpeed();e.Handled=true;e.SuppressKeyPress=true;}};
        BuildUi();detector.Interval=1000;detector.Tick+=delegate{DetectGame();};Shown+=delegate{RegisterKeys();detector.Start();DetectGame();};
    }

    string T(string simplified,string traditional,string english){return language==0?simplified:(language==1?traditional:english);}
    void BuildUi()
    {
        Controls.Clear();
        var header=new Panel{Dock=DockStyle.Top,Height=70,BackColor=Color.FromArgb(28,30,31)};Controls.Add(header);
        header.Controls.Add(new Label{Text="Age of Empires II: Definitive Edition",ForeColor=Orange,Font=new Font(Font.FontFamily,15,FontStyle.Bold),AutoSize=true,Location=new Point(68,12)});
        header.Controls.Add(new Label{Text="v101.103.54800.0 - Build 185872 V2.1  Plus 12 Trainer",ForeColor=Color.Gainsboro,Font=new Font(Font.FontFamily,10,FontStyle.Bold),AutoSize=true,Location=new Point(94,39)});
        AddLanguage(header,"简体",0,570);AddLanguage(header,"繁體",1,625);AddLanguage(header,"English",2,680);

        var left=new Panel{Location=new Point(6,74),Size=new Size(220,598),BackColor=PanelColor};Controls.Add(left);
        var cover=new PictureBox{Location=new Point(0,0),Size=new Size(220,310),BackColor=Color.FromArgb(44,48,50),BorderStyle=BorderStyle.FixedSingle,SizeMode=PictureBoxSizeMode.StretchImage};left.Controls.Add(cover);
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AoE.Cover"))if(stream!=null)using(Image image=Image.FromStream(stream))cover.Image=new Bitmap(image);
        processInfo.Location=new Point(4,322);processInfo.Size=new Size(212,150);processInfo.ForeColor=Color.Silver;left.Controls.Add(processInfo);
        left.Controls.Add(new Label{Text=T("适配 Build 185872\r\n仅限单机使用","適配 Build 185872\r\n僅限單機使用","Adapted for Build 185872\r\nSingle-player use only"),Location=new Point(4,500),Size=new Size(210,45),ForeColor=Color.Gray});
        var projectLink=new LinkLabel{Text=T("项目主页：GitHub","專案首頁：GitHub","Project page: GitHub"),Location=new Point(4,545),AutoSize=true,LinkColor=Orange,ActiveLinkColor=Color.White,Font=new Font(Font.FontFamily,8.5f,FontStyle.Underline)};
        projectLink.LinkClicked+=delegate{try{Process.Start(new ProcessStartInfo("https://github.com/KagurazakaNatsuki/AOE2-Trainer"){UseShellExecute=true});}catch(Exception e){SetStatus(()=>T("无法打开项目主页：","無法開啟專案首頁：","Cannot open project page: ")+e.Message,true);}};
        left.Controls.Add(projectLink);

        var right=new Panel{Location=new Point(232,74),Size=new Size(552,598),BackColor=Dark};Controls.Add(right);
        right.Controls.Add(new Label{Text=T("热键","熱鍵","Hotkeys"),Font=new Font(Font.FontFamily,11,FontStyle.Bold),Location=new Point(4,4),AutoSize=true});
        right.Controls.Add(new Label{Text=T("选项","選項","Options"),Font=new Font(Font.FontFamily,11,FontStyle.Bold),Location=new Point(160,4),AutoSize=true});
        right.Controls.Add(new Label{BackColor=Color.Gray,Location=new Point(0,29),Size=new Size(542,1)});
        right.Controls.Add(new Label{Text=T("特别说明：使用修改器前请先进入单人战局！","特別說明：使用修改器前請先進入單人戰局！","Special Notes: Enter a single-player match before use!"),ForeColor=Orange,Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold),Location=new Point(5,44),AutoSize=true});
        int y=78;
        AddRow(right,ref y,"Num 1",T("修改木材","修改木材","Edit Wood"),wood,delegate{SetResource(1,()=>T("木材","木材","Wood"),wood);});
        AddRow(right,ref y,"Num 2",T("修改食物","修改食物","Edit Food"),food,delegate{SetResource(0,()=>T("食物","食物","Food"),food);});
        AddRow(right,ref y,"Num 3",T("修改黄金","修改黃金","Edit Gold"),gold,delegate{SetResource(3,()=>T("黄金","黃金","Gold"),gold);});
        AddRow(right,ref y,"Num 4",T("修改石料","修改石料","Edit Stone"),stone,delegate{SetResource(2,()=>T("石料","石料","Stone"),stone);});
        AddRow(right,ref y,"Num 6",T("恢复单位与建筑生命值","恢復單位與建築生命值","Restore Units & Buildings Health"),null,RestoreHealthAction);
        AddRow(right,ref y,"Num 7",T("瞬间研发（一次）","瞬間研發（一次）","Instant Research (Once)"),null,InstantResearchAction);
        AddRow(right,ref y,"Num 8",T("瞬间建造（一次）","瞬間建造（一次）","Instant Buildings (Once)"),null,InstantBuildingsAction);
        AddRow(right,ref y,"Num 9",T("瞬间生产单位（一次）","瞬間生產單位（一次）","Instant Recruit Units (Once)"),null,InstantRecruitAction);
        AddRow(right,ref y,"Num 0",T("AI 单位与建筑设为 1 血","AI 單位與建築設為 1 血","Set AI Units & Buildings to 1 HP"),null,EmptyAiHealthAction);
        AddSpeedRow(right,ref y);
        AddRow(right,ref y,"Num +",T("最大人口上限 200","最大人口上限 200","Max Population Cap: 200"),null,MaxPopulationAction);
        AddRow(right,ref y,"Num -",T("当前人口设为 0（无限人口）","當前人口設為 0（無限人口）","Set Population To 0 (Infinite Population)"),null,ZeroPopulationAction);
        hotkeyHint.Location=new Point(0,493);hotkeyHint.Size=new Size(542,20);hotkeyHint.TextAlign=ContentAlignment.MiddleCenter;hotkeyHint.ForeColor=Color.Silver;right.Controls.Add(hotkeyHint);
        status.Location=new Point(0,520);status.Size=new Size(542,54);status.BorderStyle=BorderStyle.FixedSingle;status.Padding=new Padding(8);status.ForeColor=Color.Silver;right.Controls.Add(status);
        UpdateProcessInfo();UpdateSpeedToggle();UpdateHotkeyHint();
        if(statusMessage==null)SetStatus(()=>T("等待游戏进程……","等待遊戲進程……","Waiting for game process..."));else RenderStatus();
    }

    void AddLanguage(Panel header,string text,int id,int x){var l=new LinkLabel{Text=(language==id?"● ":"○ ")+text,LinkColor=language==id?Orange:Color.Gainsboro,ActiveLinkColor=Orange,AutoSize=true,Location=new Point(x,25),Font=new Font(Font.FontFamily,9,language==id?FontStyle.Bold:FontStyle.Regular)};l.LinkClicked+=delegate{language=id;BuildUi();};header.Controls.Add(l);}

    static NumericUpDown ResourceBox(){return new NumericUpDown{Minimum=0,Maximum=99999999,Value=999999,Increment=1000,Size=new Size(92,24),ForeColor=Color.Orange,BackColor=Color.FromArgb(30,32,34),BorderStyle=BorderStyle.FixedSingle,TextAlign=HorizontalAlignment.Center};}
    static NumericUpDown SpeedBox(){return new NumericUpDown{Minimum=0.5m,Maximum=10m,Value=1.7m,Increment=0.1m,DecimalPlaces=2};}
    void AddSpeedRow(Panel p,ref int y)
    {
        p.Controls.Add(new Label{Text="Num .",Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold),Location=new Point(5,y+4),Size=new Size(105,24)});
        speedToggle.Location=new Point(118,y);p.Controls.Add(speedToggle);
        p.Controls.Add(new Label{Text=T("游戏速度（0.5–10 倍）","遊戲速度（0.5–10 倍）","Game Speed (0.5–10x)"),Location=new Point(178,y+3),Size=new Size(235,24),Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold)});
        var less=new Label{Text="<",Location=new Point(420,y-2),Size=new Size(20,27),ForeColor=Orange,Font=new Font(Font.FontFamily,15,FontStyle.Regular),TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand};
        less.Click+=delegate{ChangeSpeed(-0.1m);};p.Controls.Add(less);
        speedEditor.Location=new Point(442,y+3);speedEditor.Size=new Size(48,22);speedEditor.BorderStyle=BorderStyle.None;speedEditor.TextAlign=HorizontalAlignment.Center;speedEditor.BackColor=Dark;speedEditor.ForeColor=Orange;speedEditor.Font=new Font(Font.FontFamily,10,FontStyle.Regular);speedEditor.Text=FormatSpeed();p.Controls.Add(speedEditor);
        p.Controls.Add(new Label{Location=new Point(442,y+23),Size=new Size(48,1),BackColor=Orange});
        var more=new Label{Text=">",Location=new Point(492,y-2),Size=new Size(20,27),ForeColor=Orange,Font=new Font(Font.FontFamily,15,FontStyle.Regular),TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand};
        more.Click+=delegate{ChangeSpeed(0.1m);};p.Controls.Add(more);y+=34;
    }
    string FormatSpeed(){return speed.Value.ToString("0.##",CultureInfo.InvariantCulture);}
    void ChangeSpeed(decimal delta)
    {
        decimal requested=Math.Min(speed.Maximum,Math.Max(speed.Minimum,speed.Value+delta));
        if(speed.Value!=requested)speed.Value=requested;
        else speedEditor.Text=FormatSpeed();
    }
    bool CommitSpeedEditor()
    {
        decimal requested;
        if(!decimal.TryParse(speedEditor.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out requested)&&
           !decimal.TryParse(speedEditor.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out requested))
        {
            speedEditor.Text=FormatSpeed();
            SetStatus(()=>T("请输入有效的游戏速度","請輸入有效的遊戲速度","Enter a valid game speed"),true);
            return false;
        }
        if(requested<speed.Minimum||requested>speed.Maximum)
        {
            speedEditor.Text=FormatSpeed();
            SetStatus(()=>T("游戏速度范围：0.5–10 倍","遊戲速度範圍：0.5–10 倍","Game speed range: 0.5–10x"),true);
            return false;
        }
        speed.Value=decimal.Round(requested,2);
        speedEditor.Text=FormatSpeed();
        return true;
    }
    void UpdateSpeedToggle()
    {
        speedToggle.IsOn=speedOverrideActive;
        speedToggle.AccessibleName=speedOverrideActive?T("关闭游戏速度","關閉遊戲速度","Disable game speed override"):T("开启游戏速度","開啟遊戲速度","Enable game speed override");
    }
    void ToggleSpeed()
    {
        if(!CommitSpeedEditor())return;
        try
        {
            if(!speedOverrideActive)
            {
                float requested=(float)speed.Value;
                using(GameContext c=Engine.Open(true))
                {
                    float original=Engine.GetGameSpeed(c);
                    Engine.SetGameSpeed(c,requested);
                    savedSpeed=original;speedPid=c.Game.Id;speedPlayer=c.Player;speedOverrideActive=true;
                }
                SetStatus(()=>T("游戏速度已开启：","遊戲速度已開啟：","Game speed enabled: ")+requested.ToString("0.##",CultureInfo.InvariantCulture)+"x");
            }
            else
            {
                using(GameContext c=Engine.Open(true))
                {
                    if(c.Game.Id!=speedPid||c.Player!=speedPlayer)throw new InvalidOperationException("对局已改变，无法恢复先前速度。");
                    Engine.SetGameSpeed(c,savedSpeed);speedOverrideActive=false;
                }
                SetStatus(()=>T("已恢复对局原速度","已恢復對局原速度","Original game speed restored"));
            }
            UpdateSpeedToggle();
        }
        catch(Exception e){SetStatus(()=>T("错误：","錯誤：","Error: ")+e.Message,true);}
    }
    void UpdateActiveSpeed()
    {
        if(!speedOverrideActive)return;
        try
        {
            float requested=(float)speed.Value;
            using(GameContext c=Engine.Open(true))
            {
                if(c.Game.Id!=speedPid||c.Player!=speedPlayer)throw new InvalidOperationException("对局已改变，无法继续修改速度。");
                Engine.SetGameSpeed(c,requested);
            }
            SetStatus(()=>T("游戏速度已设为 ","遊戲速度已設為 ","Game speed set to ")+requested.ToString("0.##",CultureInfo.InvariantCulture)+"x");
        }
        catch(Exception e){SetStatus(()=>T("错误：","錯誤：","Error: ")+e.Message,true);}
    }
    void AddRow(Panel p,ref int y,string hotkey,string option,NumericUpDown value,Action action)
    {
        p.Controls.Add(new Label{Text=hotkey,Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold),Location=new Point(5,y+4),Size=new Size(105,24)});
        var apply=new RoundedApplyButton{Text=T("应用","套用","APPLY"),Location=new Point(118,y)};apply.Click+=delegate{action();};p.Controls.Add(apply);
        p.Controls.Add(new Label{Text=option,Location=new Point(178,y+3),Size=new Size(value==null?365:235,24),Font=new Font(Font.FontFamily,9.5f,FontStyle.Bold)});
        if(value!=null){value.Location=new Point(420,y);p.Controls.Add(value);}y+=34;
    }
    void SetResource(int id,Func<string> name,NumericUpDown box)
    {
        float value=(float)box.Value;
        Run(()=>T("设置","設定","Set ")+name(),delegate{Engine.SetResource(id,value);},
            ()=>name()+T("已设为 ","已設為 "," set to ")+value.ToString("0.##",CultureInfo.InvariantCulture));
    }
    void RestoreHealthAction(){Run(()=>T("恢复生命值","恢復生命值","Restore Health"),delegate{Engine.RestoreHealth();},()=>T("生命值恢复完成","生命值恢復完成","Health restored"));}
    void InstantResearchAction(){Run(()=>T("瞬间研发","瞬間研發","Instant Research"),delegate{Engine.InstantResearchOnce();},()=>T("当前研发队列已完成","當前研發佇列已完成","Current research queue completed"));}
    void InstantBuildingsAction(){Run(()=>T("瞬间建造","瞬間建造","Instant Buildings"),delegate{Engine.InstantBuildingsOnce();},()=>T("当前建造任务已完成","當前建造任務已完成","Current construction completed"));}
    void InstantRecruitAction(){Run(()=>T("瞬间生产单位","瞬間生產單位","Instant Recruit Units"),delegate{Engine.InstantRecruitOnce();},()=>T("当前生产队列已完成","當前生產佇列已完成","Current production queue completed"));}
    void EmptyAiHealthAction(){Run(()=>T("清空 AI 生命值","清空 AI 生命值","Empty AI Health"),delegate{Engine.EmptyAiHealth();},()=>T("AI 单位与建筑已设为 1 血","AI 單位與建築已設為 1 血","AI units and buildings set to 1 HP"));}
    void MaxPopulationAction(){Run(()=>T("最大人口","最大人口","Max Population"),delegate{Engine.SetResource(4,200);},()=>T("人口上限已设为 200","人口上限已設為 200","Population cap set to 200"));}
    void ZeroPopulationAction(){Run(()=>T("人口归零","人口歸零","Zero Population"),delegate{Engine.SetResource(11,0);},()=>T("当前人口已设为 0","當前人口已設為 0","Current population set to 0"));}
    void Run(Func<string> name,Action work,Func<string> success)
    {
        SetStatus(()=>name()+T("处理中……","處理中……"," processing..."));
        Task.Run(delegate
        {
            try{work();BeginInvoke((Action)delegate{SetStatus(success);});}
            catch(Exception e){string detail=e.Message;BeginInvoke((Action)delegate{SetStatus(()=>T("错误：","錯誤：","Error: ")+detail,true);});}
        });
    }
    void SetStatus(Func<string> message,bool error=false)
    {
        statusMessage=message;statusTime=DateTime.Now;statusError=error;RenderStatus();
    }
    void RenderStatus()
    {
        if(statusMessage==null)return;
        status.Text=statusTime.ToString("HH:mm:ss")+"  "+statusMessage();
        status.ForeColor=statusError?Color.Salmon:Color.LightGreen;
    }
    void DetectGame()
    {
        Process[] ps=Process.GetProcessesByName("AoE2DE_s");
        if(ps.Length!=1)
        {
            bool wasDetected=detectedPid!=0;
            hookedPid=detectedPid=0;speedOverrideActive=false;UpdateSpeedToggle();UpdateProcessInfo();
            if(wasDetected)SetStatus(()=>T("等待游戏进程……","等待遊戲進程……","Waiting for game process..."));
            return;
        }
        int pid=ps[0].Id;detectedPid=pid;
        if(DateTime.UtcNow<nextAttachAttempt||hookedPid==pid&&DateTime.UtcNow<nextCacheRefresh){UpdateProcessInfo();return;}
        if(hookBusy)return;hookBusy=true;UpdateProcessInfo();
        Task.Run(delegate
        {
            try
            {
                int count=Engine.RefreshObjectCache();ulong player;
                using(GameContext c=Engine.Open(false))player=c.Player;
                BeginInvoke((Action)delegate
                {
                    if(speedOverrideActive&&(speedPid!=pid||speedPlayer!=player)){speedOverrideActive=false;UpdateSpeedToggle();}
                    hookedPid=pid;nextCacheRefresh=DateTime.UtcNow.AddSeconds(60);UpdateProcessInfo();
                    SetStatus(()=>T("已连接游戏，对象缓存 ","已連接遊戲，物件快取 ","Game hooked, object cache: ")+count);
                });
            }
            catch(Exception e)
            {
                string detail=e.Message;
                BeginInvoke((Action)delegate
                {
                    nextAttachAttempt=DateTime.UtcNow.AddSeconds(5);UpdateProcessInfo();
                    if(detail.StartsWith("仅支持游戏版本"))SetStatus(()=>T("仅支持游戏版本 101.103.54800.0。检测到：","僅支援遊戲版本 101.103.54800.0。偵測到：","Only game version 101.103.54800.0 is supported. Detected: ")+detail.Substring(detail.LastIndexOf('：')+1),true);
                    else if(detail.StartsWith("当前不在有效的单人对局"))
                    {
                        if(speedOverrideActive){speedOverrideActive=false;UpdateSpeedToggle();}
                        SetStatus(()=>T("已检测游戏，等待进入单人战局……","已偵測遊戲，等待進入單人戰局……","Game detected; waiting for a single-player match..."));
                    }
                });
            }
            finally{hookBusy=false;}
        });
    }
    void UpdateProcessInfo(){string state=detectedPid==0?T("等待游戏","等待遊戲","Waiting for game"):(hookedPid==detectedPid?T("已连接 / 就绪","已連接 / 就緒","Hooked / Ready"):T("已检测 / 等待进入战局","已偵測 / 等待進入戰局","Detected / waiting for match"));processInfo.Text=T("游戏进程名称：","遊戲程序名稱：","Game Process Name:")+"\r\nAoE2DE_s.exe\r\n\r\n"+T("进程 ID：","程序 ID：","Process ID: ")+(detectedPid==0?"--":detectedPid.ToString())+"\r\n\r\n"+T("状态：","狀態：","Status: ")+state;}
    static readonly int[] FeatureKeys={0x61,0x62,0x63,0x64,0x66,0x67,0x68,0x69,0x60,0x6B,0x6D,0x6E};
    bool RegisterFeatureKeys(){bool ok=true;for(int i=0;i<FeatureKeys.Length;i++)ok&=Native.RegisterHotKey(Handle,i+1,0x4000,(uint)FeatureKeys[i]);return ok;}
    void UnregisterFeatureKeys(){for(int i=1;i<=FeatureKeys.Length;i++)Native.UnregisterHotKey(Handle,i);}
    void RegisterKeys()
    {
        bool toggleOk=Native.RegisterHotKey(Handle,13,0x4000|0x2|0x4,0x24);
        bool featureOk=RegisterFeatureKeys();
        if(!toggleOk||!featureOk)SetStatus(()=>T("部分热键注册失败，请关闭其他修改器。","部分熱鍵註冊失敗，請關閉其他修改器。","Some hotkeys failed; close other trainers."),true);
    }
    void UpdateHotkeyHint()
    {
        hotkeyHint.Text=T("快捷键：Ctrl+Shift+Home 切换（当前：","快捷鍵：Ctrl+Shift+Home 切換（目前：","Hotkeys: Ctrl+Shift+Home to toggle (currently: ")+
            (hotkeysEnabled?T("启用","啟用","on"):T("关闭","關閉","off"))+T("）","）",")");
    }
    void ToggleHotkeys()
    {
        if(hotkeysEnabled)
        {
            UnregisterFeatureKeys();hotkeysEnabled=false;
            SetStatus(()=>T("功能快捷键已关闭","功能快捷鍵已關閉","Feature hotkeys disabled"));
        }
        else
        {
            bool ok=RegisterFeatureKeys();hotkeysEnabled=true;
            SetStatus(ok?new Func<string>(()=>T("功能快捷键已启用","功能快捷鍵已啟用","Feature hotkeys enabled")):
                new Func<string>(()=>T("部分热键注册失败，请关闭其他修改器。","部分熱鍵註冊失敗，請關閉其他修改器。","Some hotkeys failed; close other trainers.")),!ok);
        }
        UpdateHotkeyHint();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        detector.Stop();
        if(speedOverrideActive)try{using(GameContext c=Engine.Open(true))if(c.Game.Id==speedPid&&c.Player==speedPlayer)Engine.SetGameSpeed(c,savedSpeed);}catch{}
        UnregisterFeatureKeys();Native.UnregisterHotKey(Handle,13);base.OnFormClosed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg!=WM_HOTKEY){base.WndProc(ref m);return;}
        if(m.WParam.ToInt32()==13){ToggleHotkeys();return;}
        if(!hotkeysEnabled)return;
        switch(m.WParam.ToInt32())
        {
            case 1:SetResource(1,()=>T("木材","木材","Wood"),wood);break;
            case 2:SetResource(0,()=>T("食物","食物","Food"),food);break;
            case 3:SetResource(3,()=>T("黄金","黃金","Gold"),gold);break;
            case 4:SetResource(2,()=>T("石料","石料","Stone"),stone);break;
            case 5:RestoreHealthAction();break;
            case 6:InstantResearchAction();break;
            case 7:InstantBuildingsAction();break;
            case 8:InstantRecruitAction();break;
            case 9:EmptyAiHealthAction();break;
            case 10:MaxPopulationAction();break;
            case 11:ZeroPopulationAction();break;
            case 12:ToggleSpeed();break;
        }
    }
}

internal static class Entry
{
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new TrainerForm());}
}
