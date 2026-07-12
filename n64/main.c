#include <stdio.h>
#include <string.h>
#include <libdragon.h>

// The 39 AutoLUT calibration colors, in the exact order of savestates/PALETTE.txt
// (index i here = line i+1 there). Must stay in sync with PALETTE.txt,
// src/AutoLUT.Core/Calibration/CalibrationPalette.cs and wii/main.c.
typedef struct {
    uint8_t red;
    uint8_t green;
    uint8_t blue;
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

#define FONT_WIDTH  8
#define FONT_HEIGHT 8

// Draws text on a fixed dark-gray box so it stays readable on any palette color.
static void draw_boxed_text(surface_t *disp, int x, int y, const char *text) {
    uint32_t box_color = graphics_make_color(0x30, 0x30, 0x30, 0xFF);
    int box_width = (int)strlen(text) * FONT_WIDTH + 8;
    graphics_draw_box(disp, x, y, box_width, FONT_HEIGHT + 8, box_color);
    graphics_set_color(graphics_make_color(0xFF, 0xFF, 0xFF, 0xFF), box_color);
    graphics_draw_text(disp, x + 4, y + 4, text);
}

int main(void) {
    // 32bpp framebuffer with gamma, dedither and divot off: the commanded 8-bit
    // RGB is scanned out exactly - no RGBA5551 quantization, no dither. Resample
    // (bilinear VI upscale) cannot alter a solid-color region and must stay on:
    // disabling it at 320-wide hits a VI fetch erratum (libdragon issue #66)
    // that corrupts scanlines while the CPU is writing the other framebuffer.
    display_init(RESOLUTION_320x240, DEPTH_32_BPP, 2, GAMMA_NONE, FILTERS_RESAMPLE);
    joypad_init();
    graphics_set_default_font();

    int current_index = 0;
    // The bulk uncached framebuffer writes contend with the VI's scanout fetches,
    // which shows up as flicker near the top of the frame. Draw only when the
    // color changes - once into each of the two buffers - and leave the
    // framebuffers untouched on idle frames.
    int buffers_to_draw = 2;

    while (1) {
        joypad_poll();
        joypad_buttons_t pressed = joypad_get_buttons_pressed(JOYPAD_PORT_1);

        if (pressed.d_right || pressed.a) {
            current_index = (current_index + 1) % PALETTE_COUNT;
            buffers_to_draw = 2;
        } else if (pressed.d_left) {
            current_index = (current_index + PALETTE_COUNT - 1) % PALETTE_COUNT;
            buffers_to_draw = 2;
        }

        // display_get blocks until vblank frees a buffer, throttling this loop
        // to one iteration per frame.
        surface_t *disp = display_get();

        if (buffers_to_draw > 0) {
            buffers_to_draw--;
            const palette_color *color = &palette[current_index];
            graphics_fill_screen(disp, graphics_make_color(color->red, color->green, color->blue, 0xFF));

            // Overlays sit at the screen edges, inset for overscan and well clear
            // of the sampled center 30% region (x 112-208, y 84-156 at 320x240).
            char label[32];
            snprintf(label, sizeof(label), "%02d/%02d #%02X%02X%02X",
                     current_index + 1, PALETTE_COUNT, color->red, color->green, color->blue);

            draw_boxed_text(disp, 16, 16, "D-PAD: prev/next  A: next");
            draw_boxed_text(disp, 16, 208, label);
        }

        display_show(disp);
    }
}
