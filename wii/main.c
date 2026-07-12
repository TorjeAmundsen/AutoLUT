#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <gccore.h>
#include <wiiuse/wpad.h>

// Wii framebuffer uses Y1CbY2Cr packed format (two pixels per 32-bit word).
#define TEXT_WHITE     0xFF80FF80
#define TEXT_DARK_GRAY 0x30803080

static void *framebuffers[2] = { NULL, NULL };
static int current_framebuffer = 0;
static GXRModeObj *video_mode = NULL;

#include "font.h"

// The 39 AutoLUT calibration colors, in the exact order of savestates/PALETTE.txt
// (index i here = line i+1 there). Must stay in sync with PALETTE.txt and
// src/AutoLUT.Core/Calibration/CalibrationPalette.cs.
typedef struct {
    u8 red;
    u8 green;
    u8 blue;
} palette_color;

static const palette_color palette[] = {
    { 0x00, 0x00, 0x00 }, // 01 gray_000000
    { 0x20, 0x20, 0x20 }, // 02 gray_202020
    { 0x40, 0x40, 0x40 }, // 03 gray_404040
    { 0x60, 0x60, 0x60 }, // 04 gray_606060
    { 0x80, 0x80, 0x80 }, // 05 gray_808080
    { 0xA0, 0xA0, 0xA0 }, // 06 gray_a0a0a0
    { 0xC0, 0xC0, 0xC0 }, // 07 gray_c0c0c0
    { 0xE0, 0xE0, 0xE0 }, // 08 gray_e0e0e0
    { 0xFF, 0xFF, 0xFF }, // 09 gray_ffffff
    { 0x00, 0x00, 0x80 }, // 10 grid_000080
    { 0x00, 0x00, 0xFF }, // 11 grid_0000ff
    { 0x00, 0x80, 0x00 }, // 12 grid_008000
    { 0x00, 0x80, 0x80 }, // 13 grid_008080
    { 0x00, 0x80, 0xFF }, // 14 grid_0080ff
    { 0x00, 0xFF, 0x00 }, // 15 grid_00ff00
    { 0x00, 0xFF, 0x80 }, // 16 grid_00ff80
    { 0x00, 0xFF, 0xFF }, // 17 grid_00ffff
    { 0x80, 0x00, 0x00 }, // 18 grid_800000
    { 0x80, 0x00, 0x80 }, // 19 grid_800080
    { 0x80, 0x00, 0xFF }, // 20 grid_8000ff
    { 0x80, 0x80, 0x00 }, // 21 grid_808000
    { 0x80, 0x80, 0xFF }, // 22 grid_8080ff
    { 0x80, 0xFF, 0x00 }, // 23 grid_80ff00
    { 0x80, 0xFF, 0x80 }, // 24 grid_80ff80
    { 0x80, 0xFF, 0xFF }, // 25 grid_80ffff
    { 0xFF, 0x00, 0x00 }, // 26 grid_ff0000
    { 0xFF, 0x00, 0x80 }, // 27 grid_ff0080
    { 0xFF, 0x00, 0xFF }, // 28 grid_ff00ff
    { 0xFF, 0x80, 0x00 }, // 29 grid_ff8000
    { 0xFF, 0x80, 0x80 }, // 30 grid_ff8080
    { 0xFF, 0x80, 0xFF }, // 31 grid_ff80ff
    { 0xFF, 0xFF, 0x00 }, // 32 grid_ffff00
    { 0xFF, 0xFF, 0x80 }, // 33 grid_ffff80
    { 0xC0, 0x40, 0x40 }, // 34 mids_c04040
    { 0x40, 0xC0, 0x40 }, // 35 mids_40c040
    { 0x40, 0x40, 0xC0 }, // 36 mids_4040c0
    { 0xC0, 0xC0, 0x40 }, // 37 mids_c0c040
    { 0xC0, 0x40, 0xC0 }, // 38 mids_c040c0
    { 0x40, 0xC0, 0xC0 }, // 39 mids_40c0c0
};
#define PALETTE_COUNT ((int)(sizeof(palette) / sizeof(palette[0])))

static inline u32 rgb_to_ycbcr(u8 red1, u8 green1, u8 blue1, u8 red2, u8 green2, u8 blue2) {
    int y1 =  16 + (( 66 * red1 + 129 * green1 +  25 * blue1 + 128) >> 8);
    int y2 =  16 + (( 66 * red2 + 129 * green2 +  25 * blue2 + 128) >> 8);
    int cb = 128 + ((-38 * (int)red1 -  74 * (int)green1 + 112 * (int)blue1 + 128) >> 8);
    int cr = 128 + ((112 * (int)red1 -  94 * (int)green1 -  18 * (int)blue1 + 128) >> 8);
    return ((u32)y1 << 24) | ((u32)cb << 16) | ((u32)y2 << 8) | (u32)cr;
}

static void clear_screen(void *framebuffer, u32 color) {
    u32 *pixels = (u32 *)framebuffer;
    u32 pixel_pair_count = video_mode->fbWidth * video_mode->xfbHeight / 2;
    for (u32 i = 0; i < pixel_pair_count; i++) {
        pixels[i] = color;
    }
}

// x and width must be even: two pixels per framebuffer word, and words must be
// written whole to avoid reads from uncached XFB memory which crash on real hardware
static void fill_rect(void *framebuffer, int x, int y, int width, int height, u32 color) {
    u32 *pixels = (u32 *)framebuffer;
    int words_per_row = video_mode->fbWidth / 2;
    for (int row = 0; row < height; row++) {
        int pixel_y = y + row;
        if (pixel_y < 0 || pixel_y >= (int)video_mode->xfbHeight) {
            continue;
        }
        for (int column = 0; column < width; column += 2) {
            int pixel_x = x + column;
            if (pixel_x < 0 || pixel_x + 1 >= (int)video_mode->fbWidth) {
                continue;
            }
            pixels[pixel_y * words_per_row + pixel_x / 2] = color;
        }
    }
}

// Round up to next even number so character cells align to word boundaries
#define FONT_ADVANCE ((FONT_WIDTH + 1) & ~1)

static void draw_char(void *framebuffer, int x, int y, char character, u32 foreground, u32 background) {
    if (character < FONT_FIRST_CHAR || character > FONT_LAST_CHAR) {
        return;
    }
    int font_index = character - FONT_FIRST_CHAR;
    u32 *pixels = (u32 *)framebuffer;
    int words_per_row = video_mode->fbWidth / 2;
    u8 foreground_luminance = (foreground >> 24) & 0xFF;
    u8 background_luminance = (background >> 24) & 0xFF;

    for (int row = 0; row < FONT_HEIGHT; row++) {
        u8 bitmap_row = font_data[font_index][row];
        int pixel_y = y + row;
        if (pixel_y < 0 || pixel_y >= (int)video_mode->xfbHeight) {
            continue;
        }

        // Process two pixels at a time to write complete framebuffer words,
        // avoiding reads from uncached XFB memory which crash on real hardware
        for (int column = 0; column < FONT_ADVANCE; column += 2) {
            int pixel_x = x + column;
            if (pixel_x < 0 || pixel_x + 1 >= (int)video_mode->fbWidth) {
                continue;
            }

            int left_on = (column < FONT_WIDTH) && (bitmap_row & (0x80 >> column));
            int right_on = (column + 1 < FONT_WIDTH) && (bitmap_row & (0x80 >> (column + 1)));

            u8 left_luminance = left_on ? foreground_luminance : background_luminance;
            u8 right_luminance = right_on ? foreground_luminance : background_luminance;

            int word_index = pixel_y * words_per_row + (pixel_x / 2);
            pixels[word_index] = ((u32)left_luminance << 24) | (0x80 << 16)
                               | ((u32)right_luminance << 8) | 0x80;
        }
    }
}

static void draw_string(void *framebuffer, int x, int y, const char *text, u32 foreground, u32 background) {
    while (*text) {
        draw_char(framebuffer, x, y, *text, foreground, background);
        x += FONT_ADVANCE;
        text++;
    }
}

// Fills the screen with the palette color and draws the index/RGB label on a
// fixed dark-gray box at the bottom-left edge. AutoLUT only samples the center
// 30%x30% of the frame, so the screen edges are safe for UI.
static void render(int index) {
    void *framebuffer = framebuffers[current_framebuffer ^ 1];
    const palette_color *color = &palette[index];

    clear_screen(framebuffer, rgb_to_ycbcr(color->red, color->green, color->blue,
                                           color->red, color->green, color->blue));

    char label[32];
    snprintf(label, sizeof(label), "%02d/%02d #%02X%02X%02X",
             index + 1, PALETTE_COUNT, color->red, color->green, color->blue);

    // Insets keep the boxes out of typical CRT/capture overscan while staying
    // well clear of the sampled center region (y 168-312 at 480p).
    int box_width = (int)strlen(label) * FONT_ADVANCE + 8;
    fill_rect(framebuffer, 32, 424, box_width, FONT_HEIGHT + 12, TEXT_DARK_GRAY);
    draw_string(framebuffer, 36, 430, label, TEXT_WHITE, TEXT_DARK_GRAY);

    // Controls hint at the top edge.
    const char *controls = "LEFT/RIGHT: prev/next  A: next  HOME: exit";
    int controls_width = (int)strlen(controls) * FONT_ADVANCE + 8;
    fill_rect(framebuffer, 32, 32, controls_width, FONT_HEIGHT + 12, TEXT_DARK_GRAY);
    draw_string(framebuffer, 36, 38, controls, TEXT_WHITE, TEXT_DARK_GRAY);

    VIDEO_SetNextFramebuffer(framebuffer);
    VIDEO_Flush();
    current_framebuffer ^= 1;
}

int main(int argc, char **argv) {
    VIDEO_Init();
    WPAD_Init();
    PAD_Init();

    video_mode = VIDEO_GetPreferredMode(NULL);
    // Match the Wii VC's exact VI parameters from the start so the capture card
    // locks onto this signal once and never needs to re-lock during use.
    video_mode->viWidth   = 704;
    video_mode->viXOrigin = 8;

    framebuffers[0] = MEM_K0_TO_K1(SYS_AllocateFramebuffer(video_mode));
    framebuffers[1] = MEM_K0_TO_K1(SYS_AllocateFramebuffer(video_mode));

    VIDEO_Configure(video_mode);
    VIDEO_SetNextFramebuffer(framebuffers[current_framebuffer]);
    VIDEO_SetBlack(FALSE);
    VIDEO_Flush();
    VIDEO_WaitVSync();
    if (video_mode->viTVMode & VI_NON_INTERLACE) {
        VIDEO_WaitVSync();
    }

    int current_index = 0;
    render(current_index);

    while (1) {
        VIDEO_WaitVSync();
        WPAD_ScanPads();
        u32 gamepad_connected = PAD_ScanPads();

        u32 wiimote_pressed = WPAD_ButtonsDown(0);
        u16 gamepad_pressed = (gamepad_connected & 1) ? PAD_ButtonsDown(0) : 0;

        if ((wiimote_pressed & WPAD_BUTTON_HOME) || (gamepad_pressed & PAD_BUTTON_START)) {
            break;
        }

        int next_index = current_index;
        if ((wiimote_pressed & (WPAD_BUTTON_RIGHT | WPAD_BUTTON_A))
            || (gamepad_pressed & (PAD_BUTTON_RIGHT | PAD_BUTTON_A))) {
            next_index = (current_index + 1) % PALETTE_COUNT;
        } else if ((wiimote_pressed & WPAD_BUTTON_LEFT) || (gamepad_pressed & PAD_BUTTON_LEFT)) {
            next_index = (current_index + PALETTE_COUNT - 1) % PALETTE_COUNT;
        }

        if (next_index != current_index) {
            current_index = next_index;
            render(current_index);
        }
    }

    exit(0);
    return 0;
}
