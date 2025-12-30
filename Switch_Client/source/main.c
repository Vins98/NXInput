#include <switch.h>

#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <errno.h>
#include <netdb.h>
#include <unistd.h>
#include <time.h>

#define PORT 8192
#define CPU_CLOCK 204E6 //204MHz is the sweet spot between latency and battery life, still can be overriden by using sys-clk

char ipAddress[16];
u8 data[5];
PadState pad;

void * get_in_addr(struct sockaddr *sa) {
    if (sa->sa_family == AF_INET) {
        return &(((struct sockaddr_in*)sa)->sin_addr);
    }
    return &(((struct sockaddr_in6*)sa)->sin6_addr);
}

int broadcast(int sck, char * host, char * port) {
    uint8_t msg[] = "xbox_switch";
    char buffer[128] = {0};
    struct hostent * he;
    struct sockaddr_in remote;
    struct sockaddr_storage from;
    socklen_t from_len = sizeof(from);

    if ((he = gethostbyname(host)) == NULL) {
        printf("\nERROR: gethostbyname failed - is a connection active?\n");
        consoleUpdate(NULL);
        svcSleepThread(2000000000);
        return -1;
    }

    remote.sin_family = AF_INET;
    remote.sin_port = htons(atoi(port));
    remote.sin_addr = *(struct in_addr *)he->h_addr;
    memset(remote.sin_zero, 0, sizeof(remote.sin_zero));

    int on=1;
    setsockopt(sck, SOL_SOCKET, SO_BROADCAST, &on, sizeof(on));

    struct timeval tv;
    tv.tv_sec = 0;
    tv.tv_usec = 100000; 
    setsockopt(sck, SOL_SOCKET, SO_RCVTIMEO,&tv,sizeof(tv));

    printf("\x1b[12;1HSending broadcast to %s...", host);
    printf("\x1b[13;1HWaiting for 'xbox' reply...");
    printf("\x1b[14;1HPress B to Cancel.");
    consoleUpdate(NULL);

    time_t start_time = time(NULL); 
    int timeout_seconds = 15;

    while(1) {    
        //1. Check Timeout
        if (difftime(time(NULL), start_time) >= timeout_seconds) {
            printf("\x1b[15;1HConnection timed out.");
            consoleUpdate(NULL);
            svcSleepThread(2000000000); 
            return -1; 
        }

        //2. Check Cancellation
        padUpdate(&pad);
        u64 kDown = padGetButtonsDown(&pad);
        if (kDown & HidNpadButton_B) {
            return -1;
        }

        //3. Send&Receive
        sendto(sck, msg, sizeof(msg), 0, (struct sockaddr *)&remote, sizeof(remote));
        
        memset(buffer, 0, sizeof(buffer));
        int len = recvfrom(sck, buffer, sizeof(buffer)-1, 0, (struct sockaddr *)&from, &from_len);

        if (len > 0) {
            buffer[len] = '\0';
            if (strcmp("xbox", buffer) == 0) {
                char addr[40] = {0};
                const char * ptr = inet_ntop(from.ss_family, get_in_addr((struct sockaddr *)&from), addr, sizeof(addr));
                strcpy(ipAddress, ptr);
                return 0; 
            }
        }
        
    }
}

void updateButtonState(int s, struct sockaddr_in *si_other, int slen, u64 kHeld, u64 kDown, u64 kUp, u64 mask, const char* id_code) {
    if ((kDown & mask) || (kUp & mask)) {
        char packet[2];
        packet[0] = id_code[0]; 
        packet[1] = (kHeld & mask) ? 1 : 0; 
        sendto(s, packet, 2, 0, (struct sockaddr *) si_other, slen);
    }
}

int main(int argc, char* argv[]) {
    u8 currentIpBlock = 0;
    u8 ipBlocks [4] = {192, 168 , 1, 255};
    int s = -1; 

    socketInitializeDefault();
    consoleInit(NULL);

    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);

    appletSetScreenShotPermission(0);

    //Main connection loop 
    while(appletMainLoop()) {
        consoleClear();
        consoleUpdate(NULL); 

        if (hosversionBefore(8, 0, 0)) {
          pcvInitialize();
          pcvSetClockRate(PcvModule_CpuBus, CPU_CLOCK); 
        } else {
          ClkrstSession clkrstSession;
          clkrstInitialize();
          clkrstOpenSession(&clkrstSession, PcvModuleId_CpuBus, 3);
          clkrstSetClockRate(&clkrstSession, CPU_CLOCK);
          clkrstCloseSession(&clkrstSession);
        }
        printf("\x1b[12;1H");
        printf("   \x1b[K");
        printf("\x1b[13;1H");
        printf("   \x1b[K");
        printf("\x1b[14;1H");
        printf("   \x1b[K");
        printf("\x1b[15;1H");
        printf("   \x1b[K");
        printf("\x1b[1;1H|----------------------------------------------------|");
        printf("\x1b[2;1H|    NXInput - libnx 4.10/ViGEmBus fork by Vins98    |");
        printf("\x1b[3;1H|----------------------------------------------------|");

        printf("\x1b[5;1HPlease set up the IP address where the UDP socket should be opened!");
        printf("\x1b[6;1HIt is advisable to use your PC's local IP, as most routers block broadcast packets,");
        printf("\x1b[7;1H except DHCP, ARP, NDP or WoL. Make sure to run the NXInput server first!");
        printf("\x1b[8;1HUse DPAD to enter the IP address below and press the A button to connect.");
        printf("\x1b[9;1HZL/ZR can be used to decrement/increment the IP by 10.");

        //IP Selection UI
        while (appletMainLoop()) {
            padUpdate(&pad);
            u64 kDown = padGetButtonsDown(&pad);

            printf("\x1b[11;1HIP Address: ");
            for(int i=0; i<4; i++) {
                if(i == currentIpBlock) printf("[%d]", ipBlocks[i]);
                else printf("%d", ipBlocks[i]);
                if(i < 3) printf(".");
            }
            printf("   \x1b[K"); 

            if (kDown & HidNpadButton_Up) ipBlocks[currentIpBlock]++;
            if (kDown & HidNpadButton_Down) ipBlocks[currentIpBlock]--;
            if (kDown & HidNpadButton_ZR) ipBlocks[currentIpBlock]+=10;
            if (kDown & HidNpadButton_ZL) ipBlocks[currentIpBlock]-=10;
            if (kDown & HidNpadButton_Right && currentIpBlock < 3) currentIpBlock++;
            if (kDown & HidNpadButton_Left && currentIpBlock > 0) currentIpBlock--;
            
            if (kDown & HidNpadButton_A) break;

            consoleUpdate(NULL);
        }

        // Prepare for broadcast
        char computersIp[16];
        sprintf(computersIp, "%d.%d.%d.%d", ipBlocks[0], ipBlocks[1], ipBlocks[2], ipBlocks[3]);

        if ((s = socket(AF_INET, SOCK_DGRAM, 0)) == -1) {
            printf("\x1b[13;1HSocket failed: %d. Retrying...", errno);
            consoleUpdate(NULL);
            svcSleepThread(1000000000);
            continue;
        }

        if (broadcast(s, computersIp, "8192") == 0) {
            break; // Connection successful!
        } else {
            close(s); 
        }
    }

    printf("\x1b[13;1HConnected to: %s", ipAddress);
    printf("\x1b[14;1Press + and - to Exit.");
    printf("\x1b[15;1H");
    printf("   \x1b[K");
    
    consoleUpdate(NULL);

    struct sockaddr_in si_other;
    int slen = sizeof(si_other);
    memset((char *) &si_other, 0, sizeof(si_other));
    si_other.sin_family = AF_INET;
    si_other.sin_port = htons(PORT);
    inet_aton(ipAddress, &si_other.sin_addr);
    
    while (appletMainLoop()) {
        padUpdate(&pad);
        u64 kHeld = padGetButtons(&pad);
        u64 kDown = padGetButtonsDown(&pad);
        u64 kUp = padGetButtonsUp(&pad);

        if ((kHeld & HidNpadButton_Plus) && (kHeld & HidNpadButton_Minus)) break;

        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Up, "\x1");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Down, "\x2");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Left, "\x3");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Right, "\x4");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Minus, "\x5");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Plus, "\x6");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_StickL, "\x7");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_StickR, "\x8");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_L, "\x9");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_R, "\xA");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_A, "\xC");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_B, "\xD");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_X, "\xE");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_Y, "\xF");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_ZL, "\x10");
        updateButtonState(s, &si_other, slen, kHeld, kDown, kUp, HidNpadButton_ZR, "\x11");

        HidAnalogStickState l_stick = padGetStickPos(&pad, 0);
        HidAnalogStickState r_stick = padGetStickPos(&pad, 1);

        data[0] = 0x12;
        data[1] = l_stick.x >> 8; data[2] = l_stick.x & 0xFF;
        data[3] = l_stick.y >> 8; data[4] = l_stick.y & 0xFF;
        sendto(s, data, 5, 0, (struct sockaddr *) &si_other, slen);

        data[0] = 0x13;
        data[1] = r_stick.x >> 8; data[2] = r_stick.x & 0xFF;
        data[3] = r_stick.y >> 8; data[4] = r_stick.y & 0xFF;
        sendto(s, data, 5, 0, (struct sockaddr *) &si_other, slen);

        consoleUpdate(NULL);
        }

    socketExit();
    close(s);
    consoleExit(NULL);
    return 0;
}
