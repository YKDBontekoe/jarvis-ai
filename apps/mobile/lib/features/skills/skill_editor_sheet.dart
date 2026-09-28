part of 'skills_screen.dart';

/// Creates, edits, or imports a skill.
class SkillEditorSheet extends StatefulWidget {
  const SkillEditorSheet({
    required this.http,
    this.existing,
    this.import = false,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic>? existing;
  final bool import;

  @override
  State<SkillEditorSheet> createState() => _SkillEditorSheetState();
}

class _SkillEditorSheetState extends State<SkillEditorSheet> {
  late final _name = TextEditingController(
    text: asJsonString(widget.existing?['name']) ?? '',
  );
  late final _description = TextEditingController(
    text: asJsonString(widget.existing?['description']) ?? '',
  );
  late final _instructions = TextEditingController(
    text: asJsonString(widget.existing?['instructions']) ?? '',
  );
  final _markdown = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _instructions.dispose();
    _markdown.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      if (widget.import) {
        await widget.http.post<void>(
          '/api/v1/skills/import',
          data: {'markdown': _markdown.text},
        );
      } else if (widget.existing != null) {
        final id = jsonId(widget.existing);
        if (id == null) {
          throw const FormatException('Missing skill id.');
        }
        await widget.http.put<void>(
          '/api/v1/skills/$id',
          data: {
            'description': _description.text,
            'instructions': _instructions.text,
          },
        );
      } else {
        await widget.http.post<void>(
          '/api/v1/skills',
          data: {
            'name': _name.text,
            'description': _description.text,
            'instructions': _instructions.text,
          },
        );
      }
      if (mounted) Navigator.pop(context, true);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'The skill could not be saved.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'The skill could not be saved.');
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(
      20,
      8,
      20,
      20 + MediaQuery.of(context).viewInsets.bottom,
    ),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            widget.import
                ? 'Import SKILL.md'
                : widget.existing == null
                ? 'New skill'
                : 'Edit skill',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 6),
          Text(
            widget.import
                ? 'Paste a SKILL.md file from OpenClaw, Hermes, Claude Code, Codex, or another Jarvis.'
                : 'Saving locks the skill so Jarvis never overwrites your version.',
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
          const SizedBox(height: 16),
          if (widget.import)
            TextField(
              key: const Key('skill-markdown'),
              controller: _markdown,
              minLines: 10,
              maxLines: 18,
              decoration: const InputDecoration(
                hintText:
                    '---\nname: my-skill\ndescription: When to use it\n---\n\n1. First step…',
              ),
            )
          else ...[
            if (widget.existing == null)
              TextField(
                key: const Key('skill-name'),
                controller: _name,
                decoration: const InputDecoration(
                  labelText: 'Name',
                  helperText:
                      'Lowercase words joined by hyphens, e.g. weekly-review',
                ),
              ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('skill-description'),
              controller: _description,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'When should Jarvis use it?',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('skill-instructions'),
              controller: _instructions,
              minLines: 6,
              maxLines: 14,
              decoration: const InputDecoration(
                labelText: 'Instructions (Markdown)',
                alignLabelWithHint: true,
              ),
            ),
          ],
          if (_error != null)
            InlineNotice(
              message: _error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
          const SizedBox(height: 16),
          FilledButton(
            key: const Key('save-skill'),
            onPressed: _saving ? null : () => unawaited(_save()),
            child: Text(widget.import ? 'Import' : 'Save'),
          ),
        ],
      ),
    ),
  );
}
