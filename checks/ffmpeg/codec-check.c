/* GPL-2.0. Exercise the bundled decoder/PSMF registrations on the build host.
 * This is an initialization check, not a movie or ARM runtime test. */
#include <stdio.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>

int main(void) {
    av_register_all();
    AVCodec *codec = avcodec_find_decoder(AV_CODEC_ID_H264);
    if (!codec || !av_find_input_format("mpeg") || !av_find_input_format("h264")) return 1;
    AVCodecParserContext *parser = av_parser_init(AV_CODEC_ID_H264);
    AVCodecContext *context = avcodec_alloc_context3(codec);
    if (!parser || !context || avcodec_open2(context, codec, NULL) < 0) return 2;
    AVFrame *frame = av_frame_alloc();
    if (!frame) return 3;
    AVPacket packet;
    av_init_packet(&packet);
    packet.data = NULL;
    packet.size = 0;
    int got = -1;
    if (avcodec_decode_video2(context, frame, &got, &packet) < 0 || got != 0) return 4;
    av_frame_free(&frame);
    avcodec_free_context(&context);
    av_parser_close(parser);
    puts("PASS: bundled H.264 decoder/parser open, empty flush and MPEG-PS/raw H.264 demuxers registered (host only)");
    return 0;
}
