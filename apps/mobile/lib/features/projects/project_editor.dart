import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'project_style.dart';

/// Creates a project, or edits one when [project] is given. Pops with the
/// saved project.
class ProjectEditorScreen extends StatefulWidget {
  const ProjectEditorScreen({required this.http, this.project, super.key});

  final Dio http;
  final Map<String, dynamic>? project;

  @override
  State<ProjectEditorScreen> createState() => _ProjectEditorScreenState();
}

class _ProjectEditorScreenState extends State<ProjectEditorScreen> {
  static const _maxName = 80;
  static const _maxDescription = 500;
  static const _maxInstructions = 8000;

  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final _instructions = TextEditingController();
  String _color = 'blue';
  bool _saving = false;
  String? _error;

  bool get _editing => widget.project != null;

  @override
  void initState() {
    super.initState();
    final project = widget.project;
    if (project != null) {
      _name.text = asJsonString(project['name']) ?? '';
      _description.text = asJsonString(project['description']) ?? '';
      _instructions.text = asJsonString(project['instructions']) ?? '';
      final color = asJsonString(project['color']);
      if (projectColorKeys.contains(color)) _color = color!;
    }
  }

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _instructions.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_saving || !(_formKey.currentState?.validate() ?? false)) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = {
      'name': _name.text.trim(),
      'description': _description.text.trim(),
      'instructions': _instructions.text.trim(),
      'color': _color,
    };
    try {
      final response = _editing
          ? await widget.http.put<dynamic>(
              '/api/v1/projects/${jsonString(widget.project!, 'id')}',
              data: body,
            )
          : await widget.http.post<dynamic>('/api/v1/projects', data: body);
      if (!mounted) return;
      Navigator.pop(context, jsonObject(response.data) ?? body);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'Jarvis could not save this project.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Jarvis could not save this project.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(_editing ? 'Edit project' : 'New project'),
        actions: [
          HeaderAction(
            label: _editing ? 'Save' : 'Create',
            icon: PhosphorIconsRegular.check,
            busy: _saving,
            onPressed: () => unawaited(_save()),
          ),
        ],
      ),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 40),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Center(
                    child: AnimatedSwitcher(
                      duration: const Duration(milliseconds: 180),
                      child: ProjectBadge(
                        key: ValueKey(_color),
                        color: _color,
                        size: 64,
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                  _ColorPicker(
                    selected: _color,
                    onChanged: (value) => setState(() => _color = value),
                  ),
                  const SizedBox(height: 24),
                  if (_error != null) ...[
                    InlineNotice(message: _error!, tone: NoticeTone.danger),
                    const SizedBox(height: 16),
                  ],
                  TextFormField(
                    controller: _name,
                    autofocus: !_editing,
                    maxLength: _maxName,
                    textCapitalization: TextCapitalization.sentences,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Name',
                      hintText: 'Kitchen renovation',
                    ),
                    validator: (value) => (value ?? '').trim().isEmpty
                        ? 'Give the project a name.'
                        : null,
                  ),
                  const SizedBox(height: 8),
                  TextFormField(
                    controller: _description,
                    maxLength: _maxDescription,
                    minLines: 1,
                    maxLines: 3,
                    textCapitalization: TextCapitalization.sentences,
                    decoration: const InputDecoration(
                      labelText: 'Description',
                      hintText: 'What is this project about?',
                    ),
                  ),
                  const SizedBox(height: 20),
                  Text(
                    'Instructions',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    'Jarvis follows these in every chat and task in this project. '
                    'Describe the goal, the tone, or facts to keep in mind.',
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.4,
                      color: colors.inkSoft,
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _instructions,
                    maxLength: _maxInstructions,
                    minLines: 6,
                    maxLines: 16,
                    textCapitalization: TextCapitalization.sentences,
                    keyboardType: TextInputType.multiline,
                    decoration: const InputDecoration(
                      hintText:
                          'For example: Our budget is 15,000 euros. Compare at '
                          'least three quotes and answer in Dutch.',
                      alignLabelWithHint: true,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ColorPicker extends StatelessWidget {
  const _ColorPicker({required this.selected, required this.onChanged});

  final String selected;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) => Wrap(
    alignment: WrapAlignment.center,
    spacing: 10,
    runSpacing: 10,
    children: [
      for (final key in projectColorKeys)
        Semantics(
          button: true,
          selected: key == selected,
          label: '${projectColorName(key)} color',
          excludeSemantics: true,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: () => onChanged(key),
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 160),
              width: 36,
              height: 36,
              padding: const EdgeInsets.all(3),
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                border: Border.all(
                  color: key == selected
                      ? projectColor(context, key)
                      : Colors.transparent,
                  width: 2,
                ),
              ),
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: projectColor(context, key),
                  shape: BoxShape.circle,
                ),
                child: key == selected
                    ? const Icon(
                        PhosphorIconsRegular.check,
                        size: 16,
                        color: Colors.white,
                      )
                    : null,
              ),
            ),
          ),
        ),
    ],
  );
}
