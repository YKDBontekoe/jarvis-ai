part of 'profiles_screen.dart';

class ProfileEditorSheet extends StatefulWidget {
  const ProfileEditorSheet({required this.http, this.profile, super.key});

  final Dio http;
  final Map<String, dynamic>? profile;

  @override
  State<ProfileEditorSheet> createState() => _ProfileEditorSheetState();
}

class _ProfileEditorSheetState extends State<ProfileEditorSheet> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final _instructions = TextEditingController();
  final _preferredName = TextEditingController();
  final _replyLanguage = TextEditingController();
  bool _includeOwnerPersona = true;
  bool _restrictSkills = false;
  bool _restrictFiles = false;
  bool _includePinned = true;
  bool _contributeToLearning = true;
  bool _allowPersonaLearning = true;
  bool _allowRemember = true;
  bool _isDefault = false;
  String _memoryScope = 'all';
  String? _modelClass;
  final Set<String> _skillIds = {};
  final Set<String> _collectionIds = {};
  List<Map<String, dynamic>> _skills = const [];
  List<Map<String, dynamic>> _collections = const [];
  bool _saving = false;
  String? _error;

  bool get _editing => widget.profile != null;

  @override
  void initState() {
    super.initState();
    final profile = widget.profile;
    if (profile != null) {
      _name.text = asJsonString(profile['name']) ?? '';
      _description.text = asJsonString(profile['description']) ?? '';
      _instructions.text = asJsonString(profile['personaInstructions']) ?? '';
      _preferredName.text = asJsonString(profile['preferredName']) ?? '';
      _replyLanguage.text = asJsonString(profile['replyLanguage']) ?? '';
      _includeOwnerPersona = asJsonBool(profile['includeOwnerPersona'], true);
      _restrictSkills = asJsonBool(profile['restrictSkills']);
      _restrictFiles = asJsonBool(profile['restrictFiles']);
      _includePinned = asJsonBool(profile['includePinnedMemories'], true);
      _contributeToLearning = asJsonBool(profile['contributeToLearning'], true);
      _allowPersonaLearning = asJsonBool(profile['allowPersonaLearning'], true);
      _allowRemember = asJsonBool(profile['allowRemember'], true);
      _isDefault = asJsonBool(profile['isDefault']);
      _memoryScope = asJsonString(profile['memoryScope']) ?? 'all';
      _modelClass = asJsonString(profile['modelClass']);
      _skillIds.addAll(jsonStrings(profile['enabledSkillIds']));
      _collectionIds.addAll(jsonStrings(profile['allowedCollectionIds']));
    }
    unawaited(_loadOptions());
  }

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _instructions.dispose();
    _preferredName.dispose();
    _replyLanguage.dispose();
    super.dispose();
  }

  Future<void> _loadOptions() async {
    try {
      final skills = await widget.http.get<dynamic>('/api/v1/skills');
      final collections = await widget.http.get<dynamic>('/api/v1/collections');
      if (!mounted) return;
      setState(() {
        _skills = jsonMaps(skills.data);
        _collections = jsonMaps(collections.data);
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not load skills or collections.');
    }
  }

  Future<void> _save() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _saving = true);
    final body = {
      'name': _name.text.trim(),
      'description': _description.text.trim(),
      'personaInstructions': _instructions.text.trim(),
      'preferredName': _preferredName.text.trim(),
      'replyLanguage': _replyLanguage.text.trim(),
      'includeOwnerPersona': _includeOwnerPersona,
      'restrictSkills': _restrictSkills,
      'enabledSkillIds': _skillIds.toList(),
      'restrictFiles': _restrictFiles,
      'allowedCollectionIds': _collectionIds.toList(),
      'modelClass': _modelClass,
      'memoryScope': _memoryScope,
      'includePinnedMemories': _includePinned,
      'contributeToLearning': _contributeToLearning,
      'allowPersonaLearning': _allowPersonaLearning,
      'allowRemember': _allowRemember,
      'isDefault': _isDefault,
    };
    try {
      if (_editing) {
        await widget.http.put('/api/v1/profiles/${jsonString(widget.profile!, 'id')}', data: body);
      } else {
        await widget.http.post('/api/v1/profiles', data: body);
      }
      if (mounted) Navigator.pop(context, true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = firstProblemMessage(error.response?.data) ?? 'Could not save this profile.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not save this profile.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(_editing ? 'Edit profile' : 'New profile'),
      actions: [
        HeaderAction(
          label: 'Save',
          icon: PhosphorIconsRegular.check,
          busy: _saving,
          onPressed: _save,
        ),
      ],
    ),
    body: Form(
      key: _formKey,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
        children: [
          if (_error != null)
            InlineNotice(
              message: _error!,
              margin: const EdgeInsets.only(bottom: 12),
            ),
          TextFormField(
            controller: _name,
            maxLength: 80,
            decoration: const InputDecoration(labelText: 'Name'),
            validator: (value) =>
                value == null || value.trim().isEmpty ? 'Enter a profile name.' : null,
          ),
          TextFormField(
            controller: _description,
            maxLength: 500,
            decoration: const InputDecoration(labelText: 'Description'),
          ),
          TextFormField(
            controller: _instructions,
            minLines: 3,
            maxLines: 8,
            maxLength: 4000,
            decoration: const InputDecoration(
              labelText: 'Persona instructions',
              alignLabelWithHint: true,
              helperText: 'Owner text for this profile. It cannot bypass approvals.',
            ),
          ),
          TextFormField(
            controller: _preferredName,
            maxLength: 60,
            decoration: const InputDecoration(labelText: 'Preferred name'),
          ),
          TextFormField(
            controller: _replyLanguage,
            maxLength: 40,
            decoration: const InputDecoration(labelText: 'Reply language'),
          ),
          SwitchListTile(
            title: const Text('Include learned owner persona'),
            value: _includeOwnerPersona,
            onChanged: (value) => setState(() => _includeOwnerPersona = value),
          ),
          SwitchListTile(
            title: const Text('Limit enabled skills'),
            value: _restrictSkills,
            onChanged: (value) => setState(() => _restrictSkills = value),
          ),
          if (_restrictSkills)
            ..._skills.map((skill) {
              final id = jsonString(skill, 'id');
              if (id == null) return const SizedBox.shrink();
              return CheckboxListTile(
                title: Text(asJsonString(skill['name']) ?? id),
                subtitle: Text(asJsonString(skill['description']) ?? ''),
                value: _skillIds.contains(id),
                onChanged: (selected) => setState(() {
                  if (selected == true) {
                    _skillIds.add(id);
                  } else {
                    _skillIds.remove(id);
                  }
                }),
              );
            }),
          SwitchListTile(
            title: const Text('Limit document collections'),
            value: _restrictFiles,
            onChanged: (value) => setState(() => _restrictFiles = value),
          ),
          if (_restrictFiles)
            ..._collections.map((collection) {
              final id = jsonString(collection, 'id');
              if (id == null) return const SizedBox.shrink();
              return CheckboxListTile(
                title: Text(asJsonString(collection['name']) ?? id),
                value: _collectionIds.contains(id),
                onChanged: (selected) => setState(() {
                  if (selected == true) {
                    _collectionIds.add(id);
                  } else {
                    _collectionIds.remove(id);
                  }
                }),
              );
            }),
          const SizedBox(height: 8),
          DropdownButtonFormField<String>(
            initialValue: _memoryScope,
            decoration: const InputDecoration(labelText: 'Memory recall'),
            items: const [
              DropdownMenuItem(value: 'all', child: Text('All memories')),
              DropdownMenuItem(value: 'profile', child: Text('This profile plus pinned facts')),
              DropdownMenuItem(value: 'pinned', child: Text('Pinned facts only')),
            ],
            onChanged: (value) => setState(() => _memoryScope = value ?? 'all'),
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            initialValue: _modelClass ?? '',
            decoration: const InputDecoration(labelText: 'Model class'),
            items: const [
              DropdownMenuItem(value: '', child: Text('Inherit owner default')),
              DropdownMenuItem(value: 'chat', child: Text('Chat')),
              DropdownMenuItem(value: 'fast', child: Text('Fast')),
              DropdownMenuItem(value: 'reasoning', child: Text('Reasoning')),
            ],
            onChanged: (value) => setState(
              () => _modelClass = value == null || value.isEmpty ? null : value,
            ),
          ),
          SwitchListTile(
            title: const Text('Keep pinned owner facts available'),
            value: _includePinned,
            onChanged: (value) => setState(() => _includePinned = value),
          ),
          SwitchListTile(
            title: const Text('Contribute conversations to dreaming'),
            value: _contributeToLearning,
            onChanged: (value) => setState(() => _contributeToLearning = value),
          ),
          SwitchListTile(
            title: const Text('Allow persona learning'),
            value: _allowPersonaLearning,
            onChanged: (value) => setState(() => _allowPersonaLearning = value),
          ),
          SwitchListTile(
            title: const Text('Allow remembering facts'),
            value: _allowRemember,
            onChanged: (value) => setState(() => _allowRemember = value),
          ),
          SwitchListTile(
            title: const Text('Default profile'),
            value: _isDefault,
            onChanged: _editing && asJsonBool(widget.profile?['isDefault'])
                ? null
                : (value) => setState(() => _isDefault = value),
          ),
        ],
      ),
    ),
  );
}
