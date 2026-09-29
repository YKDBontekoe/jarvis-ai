part of 'model_settings_screen.dart';

/// Models reported by the installed Codex CLI, including the account default.
class CodexModelPicker extends StatefulWidget {
  const CodexModelPicker({
    required this.models,
    required this.selected,
    super.key,
  });

  final List<Map<String, dynamic>> models;
  final String selected;

  @override
  State<CodexModelPicker> createState() => _CodexModelPickerState();
}

class _CodexModelPickerState extends State<CodexModelPicker> {
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.trim().toLowerCase();
    final models = widget.models.where((model) {
      if (query.isEmpty) return true;
      final id =
          (asJsonString(model['model']) ?? asJsonString(model['id']) ?? '')
              .toLowerCase();
      final name = (asJsonString(model['displayName']) ?? '').toLowerCase();
      return id.contains(query) || name.contains(query);
    }).toList();
    return SafeArea(
      child: SizedBox(
        height: MediaQuery.of(context).size.height * .8,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 8),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      'Codex models',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  IconButton(
                    tooltip: 'Close',
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(PhosphorIconsRegular.x),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: TextField(
                controller: _search,
                autofocus: true,
                decoration: const InputDecoration(
                  prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass),
                  hintText: 'Search installed Codex models',
                ),
                onChanged: (_) => setState(() {}),
              ),
            ),
            const SizedBox(height: 8),
            Expanded(
              child: ListView.separated(
                itemCount: models.length + (query.isEmpty ? 1 : 0),
                separatorBuilder: (_, _) => const Divider(indent: 20),
                itemBuilder: (context, index) {
                  if (query.isEmpty && index == 0) {
                    return ListTile(
                      title: const Text('Account default'),
                      subtitle: const Text(
                        'Use the default model from the signed-in ChatGPT account.',
                      ),
                      trailing: widget.selected.isEmpty
                          ? Icon(
                              PhosphorIconsRegular.check,
                              size: 18,
                              color: JarvisColors.of(context).success,
                            )
                          : null,
                      onTap: () => Navigator.pop(context, ''),
                    );
                  }
                  final model = models[query.isEmpty ? index - 1 : index];
                  final id =
                      asJsonString(model['model']) ??
                      asJsonString(model['id']) ??
                      '';
                  final description = asJsonString(model['description']);
                  return ListTile(
                    title: Text(asJsonString(model['displayName']) ?? id),
                    subtitle: Text(
                      description == null ? id : '$id · $description',
                    ),
                    trailing: asJsonBool(model['supportsImages'])
                        ? Tooltip(
                            message: 'Accepts images',
                            child: Icon(
                              PhosphorIconsRegular.image,
                              size: 18,
                              color: JarvisColors.of(context).inkSoft,
                            ),
                          )
                        : null,
                    onTap: () => Navigator.pop(context, id),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}
