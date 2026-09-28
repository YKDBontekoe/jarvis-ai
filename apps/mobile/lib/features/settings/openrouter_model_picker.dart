part of 'model_settings_screen.dart';

/// Searchable OpenRouter catalog with context size, price, and tool support.
class OpenRouterModelPicker extends StatefulWidget {
  const OpenRouterModelPicker({
    required this.http,
    required this.title,
    super.key,
  });

  final Dio http;
  final String title;

  @override
  State<OpenRouterModelPicker> createState() => _OpenRouterModelPickerState();
}

class _OpenRouterModelPickerState extends State<OpenRouterModelPicker> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _models = const [];
  bool _loading = true;
  String? _error;
  Timer? _debounce;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/settings/models/openrouter/catalog',
        queryParameters: {
          if (_search.text.trim().isNotEmpty) 'search': _search.text.trim(),
        },
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _models = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load OpenRouter models.';
      });
    } catch (_) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load OpenRouter models.';
      });
    }
  }

  String _price(dynamic value) {
    if (value is! num) return '—';
    return value == 0 ? 'free' : '\$${value.toStringAsFixed(2)}';
  }

  @override
  Widget build(BuildContext context) => SafeArea(
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
                    widget.title,
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
                hintText: 'Search models, e.g. claude, gemini, llama',
              ),
              onChanged: (_) {
                _debounce?.cancel();
                _debounce = Timer(
                  const Duration(milliseconds: 300),
                  () => unawaited(_load()),
                );
              },
            ),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _loading && _models.isEmpty
                ? const LoadingState()
                : _error != null && _models.isEmpty
                ? ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  )
                : ListView.separated(
                    itemCount: _models.length,
                    separatorBuilder: (_, _) => const Divider(indent: 20),
                    itemBuilder: (context, index) {
                      final model = _models[index];
                      final id = asJsonString(model['id']) ?? '';
                      final contextLength = asJsonInt(model['contextLength']);
                      return ListTile(
                        title: Text(asJsonString(model['name']) ?? id),
                        subtitle: Text(
                          '$id · ${contextLength == 0 ? '?' : '${(contextLength / 1000).round()}k'} ctx · '
                          '${_price(model['promptPricePerMillion'])} in / '
                          '${_price(model['completionPricePerMillion'])} out per 1M',
                        ),
                        trailing: asJsonBool(model['supportsTools'])
                            ? const Tooltip(
                                message: 'Supports tools',
                                child: Icon(
                                  PhosphorIconsRegular.puzzlePiece,
                                  size: 18,
                                  color: JarvisColors.success,
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
