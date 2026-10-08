using System;
using System.Collections.Generic;
namespace StadiaStudio {
 public sealed class DeviceNotice {public string Title,Text;public bool Warning;public DeviceNotice(string title,string text,bool warning=false){Title=title;Text=text;Warning=warning;}}
 public sealed class DeviceNotifications {
  bool connected,candidate,low,full;DateTime changed;bool initialized;
  public List<DeviceNotice> Observe(bool present,int? battery,DateTime now){var notices=new List<DeviceNotice>();if(!initialized){initialized=true;candidate=present;changed=now;}if(candidate!=present){candidate=present;changed=now;}if(candidate!=connected&&(now-changed).TotalSeconds>=1){connected=candidate;notices.Add(new DeviceNotice(connected?"Stadia conectado":"Stadia desconectado",connected?"El mando está listo para usar.":"Se ha perdido la conexión con el mando."));}if(!connected||!present||!battery.HasValue)return notices;int value=battery.Value;if(value>20)low=false;if(value<95)full=false;if(value<=15&&!low){low=true;notices.Add(new DeviceNotice("Batería baja",value+" % de batería. Conecta el mando por USB para cargarlo.",true));}if(value==100&&!full){full=true;notices.Add(new DeviceNotice("Batería al 100 %","El mando ha alcanzado el nivel máximo de batería."));}return notices;}
 }
}


