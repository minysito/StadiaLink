// Versioned configuration shared by the driver and its desktop application.
// This module contains no WDF calls, so the real translation can be tested.
#pragma once
#include <stdint.h>
#include <string.h>

#define STADIA_CONFIG_ID 0xE2
#define STADIA_TEST_ID 0xE3
#define STADIA_CONFIG_VERSION 1
#define STADIA_BUTTON_COUNT 19
typedef struct STADIA_CONFIG {
    uint8_t Id, Version, Flags, LeftDeadzone, RightDeadzone;
    uint8_t LeftSensitivity, RightSensitivity, TriggerDeadzone;
    uint8_t StrongScale, WeakScale, TriggerThreshold, Reserved0;
    uint8_t Map[STADIA_BUTTON_COUNT];
    uint8_t Reserved[32];
    uint8_t Checksum;
} STADIA_CONFIG;

static uint8_t StadiaConfigChecksum(const STADIA_CONFIG* c) {
    const uint8_t* b=(const uint8_t*)c;uint8_t sum=0;
    for(int i=1;i<63;i++) sum^=b[i];return sum;
}
static void StadiaConfigDefault(STADIA_CONFIG* c) {
    memset(c,0,sizeof(*c));c->Id=STADIA_CONFIG_ID;c->Version=1;
    c->LeftSensitivity=c->RightSensitivity=100;
    c->StrongScale=c->WeakScale=100;c->TriggerThreshold=50;
    for(int i=0;i<STADIA_BUTTON_COUNT;i++) c->Map[i]=(uint8_t)(i+1);
    c->Map[11]=c->Map[12]=0; // Capture and Assistant have no Xbox equivalent.
    c->Checksum=StadiaConfigChecksum(c);
}
static int StadiaConfigValid(const STADIA_CONFIG* c) {
    if(sizeof(*c)!=64 || c->Id!=STADIA_CONFIG_ID || c->Version!=1 || c->Flags>63 ||
       c->LeftDeadzone>40 || c->RightDeadzone>40 || c->TriggerDeadzone>40 ||
       c->LeftSensitivity<50 || c->LeftSensitivity>200 ||
       c->RightSensitivity<50 || c->RightSensitivity>200 ||
       c->StrongScale>100 || c->WeakScale>100 ||
       c->TriggerThreshold<1 || c->TriggerThreshold>100 || c->Reserved0 ||
       c->Checksum!=StadiaConfigChecksum(c)) return 0;
    for(int i=0;i<19;i++) if(c->Map[i]>19 || c->Map[i]==12 || c->Map[i]==13) return 0;
    for(int i=0;i<32;i++) if(c->Reserved[i]) return 0;
    return 1;
}
static void StadiaPut16(uint8_t* out,int value) {out[0]=(uint8_t)value;out[1]=(uint8_t)(value>>8);}
static int StadiaAxis(uint8_t raw,int deadzone,int sensitivity,int invert) {
    int value=((raw<1?1:raw)-128)*258;
    int magnitude=value<0?-value:value;int dz=32766*deadzone/100;
    if(magnitude<=dz) return 32768;
    magnitude=(magnitude-dz)*32766/(32766-dz);
    magnitude=magnitude*sensitivity/100;
    if(magnitude>32766) magnitude=32766;
    if(value<0) magnitude=-magnitude;if(invert) magnitude=-magnitude;
    return 32768+magnitude;
}
static int StadiaTrigger(uint8_t raw,int deadzone) {
    int dz=255*deadzone/100;
    if(raw<=dz) return 0;return (raw-dz)*1023/(255-dz);
}
static void StadiaTranslate(const uint8_t* raw,const STADIA_CONFIG* c,uint8_t* out) {
    memset(out,0,16);
    for(int i=0;i<4;i++) {
        int source=(c->Flags&16)?(i+2)%4:i;
        StadiaPut16(out+2*i,StadiaAxis(raw[4+source],i<2?c->LeftDeadzone:c->RightDeadzone,
            i<2?c->LeftSensitivity:c->RightSensitivity,(c->Flags&(1<<i))!=0));
    }
    int lt=StadiaTrigger(raw[8],c->TriggerDeadzone),rt=StadiaTrigger(raw[9],c->TriggerDeadzone);
    if(c->Flags&32) {int temp=lt;lt=rt;rt=temp;}
    int physical[19]={
        !!(raw[3]&0x40),!!(raw[3]&0x20),!!(raw[3]&0x10),!!(raw[3]&0x08),
        !!(raw[3]&0x04),!!(raw[3]&0x02),!!(raw[2]&0x40),!!(raw[2]&0x20),
        !!(raw[3]&0x01),!!(raw[2]&0x80),!!(raw[2]&0x10),!!(raw[2]&0x01),!!(raw[2]&0x02),
        raw[1]==0 || raw[1]==1 || raw[1]==7,
        raw[1]==3 || raw[1]==4 || raw[1]==5,
        raw[1]==5 || raw[1]==6 || raw[1]==7,
        raw[1]==1 || raw[1]==2 || raw[1]==3,
        lt*100>=1023*c->TriggerThreshold,rt*100>=1023*c->TriggerThreshold
    };
    int targets[20]={0};
    // Preserve analog travel only for triggers assigned to trigger destinations.
    int analogL=0,analogR=0;
    for(int i=0;i<19;i++) {
        int target=c->Map[i];
        if(i>=17 && (target==18 || target==19)) {
            int travel=i==17?lt:rt;
            if(target==18 && travel>analogL) analogL=travel;
            if(target==19 && travel>analogR) analogR=travel;
        } else if(physical[i] && target) targets[target]=1;
    }
    if(targets[18]) analogL=1023;if(targets[19]) analogR=1023;
    StadiaPut16(out+8,analogL);StadiaPut16(out+10,analogR);
    int dx=targets[17]-targets[16],dy=targets[15]-targets[14];
    if(dy<0) out[12]=dx<0?8:(dx>0?2:1);
    else if(dy>0) out[12]=dx<0?6:(dx>0?4:5);
    else out[12]=dx<0?7:(dx>0?3:0);
    out[13]=(uint8_t)(targets[1]|(targets[2]<<1)|(targets[3]<<2)|(targets[4]<<3)|
        (targets[5]<<4)|(targets[6]<<5)|(targets[7]<<6)|(targets[8]<<7));
    out[14]=(uint8_t)(targets[9]|(targets[10]<<1));out[15]=(uint8_t)targets[11];
}
