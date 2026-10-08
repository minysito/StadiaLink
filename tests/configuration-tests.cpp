#include "../driver/configuration.h"
#include <stdio.h>
#include <stdlib.h>
static int checks=0;
static void Assert(bool value,const char* text) {checks++;if(!value){printf("FAIL: %s\n",text);exit(1);}}
static int Read16(const uint8_t* b){return b[0]|(b[1]<<8);}
int main() {
    STADIA_CONFIG c;StadiaConfigDefault(&c);Assert(sizeof(c)==64,"ABI size");Assert(StadiaConfigValid(&c),"default accepted");
    uint8_t raw[10]={3,8,0,0,128,128,128,128,0,0},out[16];
    StadiaTranslate(raw,&c,out);for(int i=0;i<4;i++)Assert(Read16(out+2*i)==32768,"idle axes centered");Assert(out[12]==0 && out[13]==0 && out[14]==0 && out[15]==0,"idle buttons released");
    raw[3]=64;StadiaTranslate(raw,&c,out);Assert(out[13]==1,"default A");c.Map[0]=2;StadiaTranslate(raw,&c,out);Assert(out[13]==2,"A to B");c.Map[0]=0;StadiaTranslate(raw,&c,out);Assert(out[13]==0,"A disabled");
    raw[3]=0;raw[2]=1;c.Map[11]=1;StadiaTranslate(raw,&c,out);Assert(out[13]==1,"capture mapped to A");
    raw[2]=2;c.Map[12]=8;StadiaTranslate(raw,&c,out);Assert(out[13]==128,"assistant mapped to Menu");raw[2]=0;StadiaConfigDefault(&c);
    raw[8]=128;raw[9]=64;StadiaTranslate(raw,&c,out);Assert(Read16(out+8)==128*1023/255 && Read16(out+10)==64*1023/255,"independent analog triggers");
    c.Map[17]=1;StadiaTranslate(raw,&c,out);Assert(Read16(out+8)==0 && (out[13]&1)!=0,"trigger to digital threshold");raw[8]=100;StadiaTranslate(raw,&c,out);Assert((out[13]&1)==0,"trigger below threshold");
    StadiaConfigDefault(&c);c.Map[0]=18;raw[3]=64;StadiaTranslate(raw,&c,out);Assert(Read16(out+8)==1023,"button to full analog trigger");raw[3]=0;
    c.Flags=32;raw[8]=255;raw[9]=0;StadiaTranslate(raw,&c,out);Assert(Read16(out+8)==0 && Read16(out+10)==1023,"swap triggers");
    StadiaConfigDefault(&c);raw[8]=raw[9]=0;raw[1]=1;StadiaTranslate(raw,&c,out);Assert(out[12]==2,"diagonal dpad");raw[1]=8;
    c.Map[0]=14;c.Map[1]=15;raw[3]=64|32;StadiaTranslate(raw,&c,out);Assert(out[12]==0,"opposite dpad cancellation");raw[3]=0;
    // Exhaustive axis bounds, monotonicity and inversion across the supported
    // settings range, including full deflection and an exact center.
    for(int dz=0;dz<=40;dz++)for(int sens=50;sens<=200;sens++) {
        int previous=0;
        for(int r=0;r<=255;r++) {
            int v=StadiaAxis((uint8_t)r,dz,sens,0),inv=StadiaAxis((uint8_t)r,dz,sens,1);
            Assert(v>=2 && v<=65534,"axis bounds");Assert(v>=previous,"axis monotonic");Assert(v+inv==65536,"axis inversion symmetry");previous=v;
        }
        Assert(StadiaAxis(128,dz,sens,0)==32768,"axis exact center");
    }
    c.LeftSensitivity=0;c.Checksum=StadiaConfigChecksum(&c);Assert(!StadiaConfigValid(&c),"reject zero sensitivity");
    StadiaConfigDefault(&c);c.Map[0]=255;c.Checksum=StadiaConfigChecksum(&c);Assert(!StadiaConfigValid(&c),"reject invalid destination");
    StadiaConfigDefault(&c);c.Checksum^=1;Assert(!StadiaConfigValid(&c),"reject corrupt report");
    printf("PASS: %d checks against the driver's actual translation code.\n",checks);return 0;
}
