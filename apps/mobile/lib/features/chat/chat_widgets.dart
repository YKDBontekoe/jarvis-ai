import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';

import '../../http_urls.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../entities/entity_ref.dart';
import 'chat_entries.dart';
import 'image_paste_stub.dart'
    if (dart.library.js_interop) 'image_paste_web.dart';
import 'incoming_photos.dart';
import 'mcp_setup.dart';
import 'tool_catalog.dart';

part 'chat_message_widgets.dart';
part 'chat_streaming.dart';
part 'chat_tool_run.dart';
part 'chat_approval_card.dart';
part 'chat_composer.dart';
part 'chat_photos.dart';
