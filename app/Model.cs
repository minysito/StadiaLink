using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace StadiaStudio {
    [DataContract] public sealed class Profile {
        [DataMember] public int Version=1;
        [DataMember] public string Name="Estándar";
        [DataMember] public int Flags=0, LeftDeadzone=0, RightDeadzone=0;
        [DataMember] public int LeftSensitivity=100, RightSensitivity=100, TriggerDeadzone=0;
        [DataMember] public int StrongScale=100, WeakScale=100, TriggerThreshold=50;
        [DataMember] public int[] Map={1,2,3,4,5,6,7,8,9,10,11,0,0,14,15,16,17,18,19};
        [DataMember(EmitDefaultValue=false)] public int[] Actions;
        [DataMember(EmitDefaultValue=false)] public int[] Keys;
        [DataMember(EmitDefaultValue=false)] public int[] Modifiers;
        public void EnsureActions(){if(Actions==null)Actions=new int[19];if(Keys==null)Keys=new int[19];if(Modifiers==null)Modifiers=new int[19];}

        public double Axis(byte raw,int axis) {
            int dz=axis<2?LeftDeadzone:RightDeadzone;
            int gain=axis<2?LeftSensitivity:RightSensitivity;
            return AxisValue(raw,dz,gain,(Flags&(1<<axis))!=0);
        }
        public static double AxisValue(byte raw,int deadzone,int sensitivity,bool invert) {
            int value=(Math.Max(1,(int)raw)-128)*258;
            int magnitude=Math.Abs(value),dz=32766*deadzone/100;
            if(magnitude<=dz)return 0;
            magnitude=(magnitude-dz)*32766/(32766-dz);
            magnitude=Math.Min(32766,magnitude*sensitivity/100);
            return (value<0?-1:1)*(invert?-1:1)*magnitude/32766.0;
        }
        public Profile Clone() { return FromJson(ToJson()); }
        public void Validate() {
            if(Version!=1 || String.IsNullOrWhiteSpace(Name) || Name.Length>60 || Name.Any(Char.IsControl) ||
                Flags<0 || Flags>63 || LeftDeadzone<0 || LeftDeadzone>40 || RightDeadzone<0 || RightDeadzone>40 ||
                LeftSensitivity<50 || LeftSensitivity>200 || RightSensitivity<50 || RightSensitivity>200 ||
                TriggerDeadzone<0 || TriggerDeadzone>40 || StrongScale<0 || StrongScale>100 || WeakScale<0 || WeakScale>100 ||
                TriggerThreshold<1 || TriggerThreshold>100 || Map==null || Map.Length!=19 ||
                Map.Any(x=>x<0 || x>19 || x==12 || x==13)) throw new InvalidDataException("El perfil contiene valores no válidos.");
            EnsureActions();
            if(Actions.Length!=19 || Keys.Length!=19 || Modifiers.Length!=19 || Actions.Any(x=>x<0||x>14)||Modifiers.Any(x=>x<0||x>15))throw new InvalidDataException("Acción no válida.");
            if(Keys.Any(x=>x<0||x>255))throw new InvalidDataException("Tecla no válida.");
            for(int i=0;i<19;i++)if(Actions[i]!=0 && (Map[i]!=0 || (Actions[i]>=13 && !KeySupported(Keys[i]))))throw new InvalidDataException("La acción de Windows requiere una entrada sin asignación Xbox y una tecla válida.");
        }
        public static bool KeySupported(int k){return (k>=65&&k<=90)||(k>=48&&k<=57)||(k>=112&&k<=123)||new[]{13,32,27,9,8,37,38,39,40}.Contains(k);}
        public byte[] ToReport() {
            Validate();byte[] b=new byte[64];b[0]=0xE2;b[1]=1;b[2]=(byte)Flags;
            b[3]=(byte)LeftDeadzone;b[4]=(byte)RightDeadzone;b[5]=(byte)LeftSensitivity;b[6]=(byte)RightSensitivity;
            b[7]=(byte)TriggerDeadzone;b[8]=(byte)StrongScale;b[9]=(byte)WeakScale;b[10]=(byte)TriggerThreshold;
            for(int i=0;i<19;i++) b[12+i]=(byte)Map[i];for(int i=1;i<63;i++) b[63]^=b[i];return b;
        }
        public static Profile FromReport(byte[] b) {
            if(b==null || b.Length!=64 || b[0]!=0xE2 || b[1]!=1) throw new InvalidDataException("Actualiza el controlador para configurar el mando.");
            byte sum=0;for(int i=1;i<63;i++) sum^=b[i];
            if(sum!=b[63] || b[11]!=0 || b.Skip(31).Take(32).Any(x=>x!=0)) throw new InvalidDataException("Respuesta de configuración no válida.");
            Profile p=new Profile();p.Flags=b[2];p.LeftDeadzone=b[3];p.RightDeadzone=b[4];p.LeftSensitivity=b[5];p.RightSensitivity=b[6];
            p.TriggerDeadzone=b[7];p.StrongScale=b[8];p.WeakScale=b[9];p.TriggerThreshold=b[10];
            for(int i=0;i<19;i++) p.Map[i]=b[12+i];p.Validate();return p;
        }
        public string ToJson() {
            Validate();using(MemoryStream m=new MemoryStream()) {new DataContractJsonSerializer(typeof(Profile)).WriteObject(m,this);return Encoding.UTF8.GetString(m.ToArray());}
        }
        public static Profile FromJson(string json) {
            if(json==null || json.Length>65536) throw new InvalidDataException("El archivo de perfil es demasiado grande.");
            using(MemoryStream m=new MemoryStream(Encoding.UTF8.GetBytes(json))) {
                Profile p=(Profile)new DataContractJsonSerializer(typeof(Profile)).ReadObject(m);if(p==null) throw new InvalidDataException("Perfil vacío.");p.Validate();return p;
            }
        }
    }
    [DataContract] public sealed class ProfileLibrary {
        [DataMember] public string Selected="Estándar";
        [DataMember(EmitDefaultValue=false)] public string Applied;
        [DataMember] public List<Profile> Profiles=new List<Profile>();
        public static string DirectoryPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StadiaStudio");}}
        public static string FilePath {get{return Path.Combine(DirectoryPath,"profiles.json");}}
        public static ProfileLibrary Load() {
            if(!File.Exists(FilePath)) return Defaults();
            using(FileStream f=File.OpenRead(FilePath)) {
                if(f.Length>1024*1024) throw new InvalidDataException("La biblioteca de perfiles es demasiado grande.");
                ProfileLibrary l=(ProfileLibrary)new DataContractJsonSerializer(typeof(ProfileLibrary)).ReadObject(f);
                if(l==null || l.Profiles==null || l.Profiles.Count<1 || l.Profiles.Count>100) throw new InvalidDataException("Biblioteca no válida.");
                foreach(Profile p in l.Profiles) p.Validate();
                if(l.Profiles.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=l.Profiles.Count) throw new InvalidDataException("Hay nombres de perfil duplicados.");
                return l;
            }
        }
        public static ProfileLibrary Defaults() {ProfileLibrary l=new ProfileLibrary();l.Profiles.Add(new Profile());return l;}
        public void Save() {
            Directory.CreateDirectory(DirectoryPath);string tmp=FilePath+".tmp";
            using(FileStream f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)) {
                new DataContractJsonSerializer(typeof(ProfileLibrary)).WriteObject(f,this);f.Flush(true);
            }
            if(File.Exists(FilePath)) File.Replace(tmp,FilePath,FilePath+".bak");else File.Move(tmp,FilePath);
        }
    }
    public sealed class Destination {
        public int Id;public string Name;
        public Destination(int id,string name){Id=id;Name=name;}
        public override string ToString(){return I18n.T(Name);}
        public static readonly string[] Physical={"A","B","X","Y","L1","R1","Opciones","Menú","L3","R3","Stadia","Captura","Asistente","Cruceta ↑","Cruceta ↓","Cruceta ←","Cruceta →","L2","R2"};
        public static readonly Destination[] All={new Destination(0,"Sin asignar"),new Destination(1,"A"),new Destination(2,"B"),new Destination(3,"X"),new Destination(4,"Y"),
            new Destination(5,"LB"),new Destination(6,"RB"),new Destination(7,"View"),new Destination(8,"Menu"),new Destination(9,"LS · pulsación"),new Destination(10,"RS · pulsación"),
            new Destination(11,"Xbox · guía"),new Destination(14,"Cruceta ↑"),new Destination(15,"Cruceta ↓"),new Destination(16,"Cruceta ←"),new Destination(17,"Cruceta →"),new Destination(18,"LT"),new Destination(19,"RT")};
        public static string Label(int id){Destination d=All.FirstOrDefault(x=>x.Id==id);return d==null?"Sin asignar":I18n.T(d.Name);}
    }
    public sealed class ExtraAction {
        public int Id;public string Name;public ExtraAction(int id,string name){Id=id;Name=name;}public override string ToString(){return I18n.T(Name);}
        public static readonly ExtraAction[] All={new ExtraAction(0,"Control del mando"),new ExtraAction(1,"Subir volumen"),new ExtraAction(2,"Bajar volumen"),new ExtraAction(3,"Silenciar audio"),new ExtraAction(4,"Reproducir / pausar"),new ExtraAction(5,"Pista siguiente"),new ExtraAction(6,"Pista anterior"),new ExtraAction(7,"Recorte de pantalla"),new ExtraAction(8,"Xbox Game Bar"),new ExtraAction(9,"Mostrar escritorio"),new ExtraAction(10,"Cambiar de ventana"),new ExtraAction(11,"Atrás"),new ExtraAction(12,"Adelante"),new ExtraAction(13,"Tecla mantenida / pulsar para hablar"),new ExtraAction(14,"Atajo al pulsar")};
    }
}


