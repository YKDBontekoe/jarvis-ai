import 'dart:async';

import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../files/files_screen.dart';
import '../../json_maps.dart';
import '../tasks/task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'project_editor.dart';
import 'project_style.dart';

part 'project_screen_parts.dart';

enum ProjectTab { chats, files, tasks }

/// One project: its instructions, and the chats, files and tasks in it.
class ProjectScreen extends StatefulWidget {
  const ProjectScreen({
    required this.http,
    required this.projectId,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final String projectId;

  /// Opens a chat in the main chat view. Without it, chats cannot be opened
  /// from here.
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<ProjectScreen> createState() => _ProjectScreenState();
}

class _ProjectScreenState extends State<ProjectScreen> {
  Map<String, dynamic>? _project;
  List<Map<String, dynamic>> _conversations = const [];
  List<Map<String, dynamic>> _files = const [];
  List<Map<String, dynamic>> _tasks = const [];
  ProjectTab _tab = ProjectTab.chats;
  bool _loading = true;
  bool _missing = false;
  bool _busy = false;
  String? _error;
  int _requestRevision = 0;

  String get _base => '/api/v1/projects/${widget.projectId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(_base);
      if (!mounted || revision != _requestRevision) return;
      final body = jsonObject(response.data);
      setState(() {
        _project = jsonObject(body?['project']);
        _conversations = _withIds(body?['conversations']);
        _files = _withIds(body?['files']);
        _tasks = _withIds(body?['tasks']);
        _loading = false;
        _missing = _project == null;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _missing = error.response?.statusCode == 404;
        _error = 'Jarvis could not load this project.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Jarvis could not load this project.';
      });
    }
  }

  static List<Map<String, dynamic>> _withIds(dynamic data) =>
      jsonMaps(data).where((item) => jsonString(item, 'id') != null).toList();

  /// Runs one change at a time, shows its failure, and reloads afterwards.
  Future<void> _run(Future<void> Function() action, String failure) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error = firstProblemMessage(error.response?.data) ?? failure,
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = failure);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
    if (mounted) await _load();
  }

  Future<void> _newChat() async {
    final open = widget.onOpenConversation;
    if (open == null || _busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/conversations',
        data: {'title': 'New conversation', 'projectId': widget.projectId},
      );
      final id = jsonId(jsonObject(response.data));
      if (id == null) throw const FormatException('Missing conversation ID.');
      if (mounted) setState(() => _busy = false);
      await open(id);
    } catch (_) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = 'Jarvis could not start a chat in this project.';
        });
      }
    }
  }

  Future<void> _openChat(String id) async {
    final open = widget.onOpenConversation;
    if (open != null) await open(id);
  }

  Future<void> _openTask(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => TaskDetailsScreen(http: widget.http, taskId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _edit() async {
    final project = _project;
    if (project == null) return;
    final saved = await Navigator.of(context).push<Map<String, dynamic>>(
      MaterialPageRoute(
        builder: (_) =>
            ProjectEditorScreen(http: widget.http, project: project),
      ),
    );
    if (saved != null && mounted) {
      setState(() => _project = {...project, ...saved});
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete project?',
      message:
          'Its chats, files and tasks stay in Jarvis. They just leave the '
          'project and its instructions.',
      cancelLabel: 'Keep project',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    setState(() => _busy = true);
    try {
      await widget.http.delete<void>(_base);
      if (mounted) Navigator.of(context).pop();
    } catch (_) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = 'Jarvis could not delete this project.';
        });
      }
    }
  }

  /// Moves a chat, file or task into this project, or out of it.
  Future<void> _assign(String kind, String id, {required bool add}) => _run(
    () => widget.http.put<void>(
      '/api/v1/$kind/$id/project',
      data: {'projectId': add ? widget.projectId : null},
    ),
    add
        ? 'Jarvis could not add that to the project.'
        : 'Jarvis could not remove that from the project.',
  );

  Future<void> _addExistingChat() async {
    final List<Map<String, dynamic>> candidates;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/conversations');
      candidates = _withIds(
        response.data,
      ).where((item) => item['projectId'] != widget.projectId).toList();
    } catch (_) {
      if (mounted) setState(() => _error = 'Jarvis could not load your chats.');
      return;
    }
    if (!mounted) return;
    final id = await _pick(
      title: 'Add a chat',
      empty: 'Every chat is already in this project.',
      items: [
        for (final chat in candidates)
          (
            id: jsonString(chat, 'id')!,
            title: asJsonString(chat['title']) ?? 'New conversation',
            subtitle: chat['projectId'] is String
                ? 'In another project · ${_shortDate(chat['updatedAt'])}'
                : _shortDate(chat['updatedAt']),
            icon: PhosphorIconsRegular.chatCircle,
          ),
      ],
    );
    if (id != null) await _assign('conversations', id, add: true);
  }

  Future<void> _addExistingFile() async {
    final List<Map<String, dynamic>> candidates;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/files');
      final inProject = _files.map((file) => file['id']).toSet();
      candidates = _withIds(
        response.data,
      ).where((file) => !inProject.contains(file['id'])).toList();
    } catch (_) {
      if (mounted) setState(() => _error = 'Jarvis could not load your files.');
      return;
    }
    if (!mounted) return;
    final id = await _pick(
      title: 'Add a file',
      empty: 'Every file is already in this project. Upload a new one instead.',
      items: [
        for (final file in candidates)
          (
            id: jsonString(file, 'id')!,
            title: asJsonString(file['fileName']) ?? 'File',
            subtitle: _fileSubtitle(file),
            icon: _fileIcon(file),
          ),
      ],
    );
    if (id != null) await _assign('files', id, add: true);
  }

  Future<void> _upload() async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: uploadableFileExtensions,
    );
    if (file == null || !mounted) return;
    final bytes = await file.readAsBytes();
    if (!mounted) return;
    if (bytes.isEmpty || bytes.length > maxUploadBytes) {
      setState(() => _error = 'Choose a non-empty file up to 20 MB.');
      return;
    }
    await _run(() async {
      final response = await widget.http.post<dynamic>(
        '/api/v1/files',
        data: FormData.fromMap({
          'file': MultipartFile.fromBytes(
            bytes,
            filename: file.name,
            contentType: DioMediaType.parse(uploadContentTypeFor(file.name)),
          ),
        }),
        options: Options(
          sendTimeout: const Duration(minutes: 2),
          receiveTimeout: const Duration(minutes: 2),
        ),
      );
      final id = jsonId(jsonObject(response.data));
      if (id == null) throw const FormatException('Missing file ID.');
      await widget.http.put<void>(
        '/api/v1/files/$id/project',
        data: {'projectId': widget.projectId},
      );
    }, 'Jarvis could not upload this file.');
  }

  Future<void> _newTask() async {
    final task = await showDialog<({String title, String prompt})>(
      context: context,
      builder: (_) => const _NewProjectTaskDialog(),
    );
    if (task == null || !mounted) return;
    await _run(() async {
      await widget.http.post<dynamic>(
        '/api/v1/tasks',
        data: {
          'title': task.title,
          'prompt': task.prompt,
          'projectId': widget.projectId,
        },
      );
      if (mounted) setState(() => _tab = ProjectTab.tasks);
    }, 'Jarvis could not start this task.');
  }

  Future<String?> _pick({
    required String title,
    required String empty,
    required List<_PickItem> items,
  }) => showModalBottomSheet<String>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _PickerSheet(title: title, empty: empty, items: items),
  );

  @override
  Widget build(BuildContext context) {
    final project = _project;
    return Scaffold(
      appBar: AppBar(
        title: Text(
          project == null ? 'Project' : '',
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (project != null) ...[
            IconButton(
              tooltip: 'Edit project',
              onPressed: _busy ? null : () => unawaited(_edit()),
              icon: const Icon(PhosphorIconsRegular.pencilSimple, size: 20),
            ),
            PopupMenuButton<String>(
              tooltip: 'More',
              enabled: !_busy,
              icon: const Icon(PhosphorIconsRegular.dotsThree, size: 22),
              onSelected: (value) {
                if (value == 'delete') unawaited(_delete());
              },
              itemBuilder: (context) => [
                PopupMenuItem(
                  value: 'delete',
                  child: Row(
                    children: [
                      Icon(
                        PhosphorIconsRegular.trash,
                        size: 18,
                        color: JarvisColors.of(context).danger,
                      ),
                      const SizedBox(width: 12),
                      Text(
                        'Delete project',
                        style: TextStyle(
                          color: JarvisColors.of(context).danger,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(width: 6),
          ],
        ],
      ),
      body: _body(project),
    );
  }

  Widget _body(Map<String, dynamic>? project) {
    if (project == null) {
      if (_loading) return const LoadingState();
      if (_missing) {
        return const EmptyState(
          icon: PhosphorIconsRegular.folderOpen,
          title: 'This project is gone',
          message:
              'It was deleted. Its chats, files and tasks are still in Jarvis.',
        );
      }
      return ErrorState(
        message: _error ?? 'Jarvis could not load this project.',
        onRetry: _load,
      );
    }
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 40),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FadeSlideIn(child: _ProjectHeader(project: project)),
                const SizedBox(height: 20),
                FadeSlideIn(
                  index: 1,
                  child: _NewChatCard(
                    color: asJsonString(project['color']),
                    busy: _busy,
                    onTap: widget.onOpenConversation == null
                        ? null
                        : () => unawaited(_newChat()),
                  ),
                ),
                const SizedBox(height: 12),
                FadeSlideIn(
                  index: 2,
                  child: _InstructionsCard(
                    instructions: asJsonString(project['instructions']),
                    onEdit: () => unawaited(_edit()),
                  ),
                ),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  InlineNotice(
                    message: _error!,
                    tone: NoticeTone.danger,
                    actions: [
                      TextButton(
                        onPressed: () => setState(() => _error = null),
                        child: const Text('Dismiss'),
                      ),
                    ],
                  ),
                ],
                const SizedBox(height: 24),
                SegmentedButton<ProjectTab>(
                  showSelectedIcon: false,
                  segments: [
                    ButtonSegment(
                      value: ProjectTab.chats,
                      label: Text(_tabLabel('Chats', _conversations.length)),
                    ),
                    ButtonSegment(
                      value: ProjectTab.files,
                      label: Text(_tabLabel('Files', _files.length)),
                    ),
                    ButtonSegment(
                      value: ProjectTab.tasks,
                      label: Text(_tabLabel('Tasks', _tasks.length)),
                    ),
                  ],
                  selected: {_tab},
                  onSelectionChanged: (value) =>
                      setState(() => _tab = value.first),
                ),
                const SizedBox(height: 14),
                AnimatedSwitcher(
                  duration: const Duration(milliseconds: 180),
                  child: KeyedSubtree(
                    key: ValueKey(_tab),
                    child: switch (_tab) {
                      ProjectTab.chats => _chats(),
                      ProjectTab.files => _filesSection(),
                      ProjectTab.tasks => _tasksSection(),
                    },
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static String _tabLabel(String label, int count) =>
      count == 0 ? label : '$label  $count';

  Widget _chats() => _Section(
    emptyIcon: PhosphorIconsRegular.chatsCircle,
    emptyTitle: 'No chats yet',
    emptyMessage:
        'Start a chat above, or bring in one you already had. Jarvis follows '
        'the project instructions in each of them.',
    actions: [
      _SectionAction(
        icon: PhosphorIconsRegular.plus,
        label: 'Add existing chat',
        onPressed: _busy ? null : () => unawaited(_addExistingChat()),
      ),
    ],
    rows: [
      for (final chat in _conversations)
        _ItemRow(
          icon: chat['pinned'] == true
              ? PhosphorIconsFill.pushPin
              : PhosphorIconsRegular.chatCircle,
          title: asJsonString(chat['title']) ?? 'New conversation',
          subtitle: _shortDate(chat['updatedAt']),
          onTap: widget.onOpenConversation == null
              ? null
              : () => unawaited(_openChat(jsonString(chat, 'id')!)),
          onRemove: _busy
              ? null
              : () => unawaited(
                  _assign('conversations', jsonString(chat, 'id')!, add: false),
                ),
        ),
    ],
  );

  Widget _filesSection() => _Section(
    emptyIcon: PhosphorIconsRegular.folderOpen,
    emptyTitle: 'No files yet',
    emptyMessage:
        'Add notes, quotes or plans. Jarvis sees them in every chat here and '
        'searches them when you ask.',
    actions: [
      _SectionAction(
        icon: PhosphorIconsRegular.uploadSimple,
        label: 'Upload',
        primary: true,
        onPressed: _busy ? null : () => unawaited(_upload()),
      ),
      _SectionAction(
        icon: PhosphorIconsRegular.plus,
        label: 'Add from Files',
        onPressed: _busy ? null : () => unawaited(_addExistingFile()),
      ),
    ],
    rows: [
      for (final file in _files)
        _ItemRow(
          icon: _fileIcon(file),
          title: asJsonString(file['fileName']) ?? 'File',
          subtitle: _fileSubtitle(file),
          onRemove: _busy
              ? null
              : () => unawaited(
                  _assign('files', jsonString(file, 'id')!, add: false),
                ),
        ),
    ],
  );

  Widget _tasksSection() => _Section(
    emptyIcon: PhosphorIconsRegular.listChecks,
    emptyTitle: 'No tasks yet',
    emptyMessage:
        'Hand Jarvis longer work for this project. It runs in the background '
        'with the project instructions and files.',
    actions: [
      _SectionAction(
        icon: PhosphorIconsRegular.plus,
        label: 'New task',
        primary: true,
        onPressed: _busy ? null : () => unawaited(_newTask()),
      ),
    ],
    rows: [
      for (final task in _tasks)
        _ItemRow(
          icon: statusStyle(asJsonString(task['status']) ?? '').icon,
          title: asJsonString(task['title']) ?? 'Task',
          subtitle: _shortDate(task['createdAt']),
          trailing: StatusPill.forStatus(asJsonString(task['status']) ?? ''),
          onTap: () => unawaited(_openTask(jsonString(task, 'id')!)),
          onRemove: _busy
              ? null
              : () => unawaited(
                  _assign('tasks', jsonString(task, 'id')!, add: false),
                ),
        ),
    ],
  );
}

IconData _fileIcon(Map<String, dynamic> file) {
  final type = asJsonString(file['contentType']) ?? '';
  if (type == 'application/pdf') return PhosphorIconsRegular.filePdf;
  if (type.startsWith('image/')) return PhosphorIconsRegular.fileImage;
  return PhosphorIconsRegular.fileText;
}

String _fileSubtitle(Map<String, dynamic> file) {
  final bytes = asJsonInt(file['sizeBytes']);
  final size = bytes >= 1024 * 1024
      ? '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB'
      : '${(bytes / 1024).ceil()} KB';
  final status = asJsonString(file['processingStatus']) ?? '';
  return [
    size,
    if (status.isNotEmpty && status != 'indexed') statusStyle(status).label,
  ].join(' · ');
}

const _months = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/// "Today", "Yesterday", "3 Oct", or "3 Oct 2025" for other years.
String _shortDate(Object? value, [DateTime? now]) {
  final date = jsonDate(value, local: true);
  if (date == null) return '';
  final today = now ?? DateTime.now();
  final days = DateTime(
    today.year,
    today.month,
    today.day,
  ).difference(DateTime(date.year, date.month, date.day)).inDays;
  if (days == 0) return 'Today';
  if (days == 1) return 'Yesterday';
  final label = '${date.day} ${_months[date.month - 1]}';
  return date.year == today.year ? label : '$label ${date.year}';
}
