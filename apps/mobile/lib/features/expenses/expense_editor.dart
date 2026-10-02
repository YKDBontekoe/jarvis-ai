import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/chat_screen.dart' show photoContentType, photoFileName;
import '../chat/chat_widgets.dart' show PhotoViewerPage;
import 'expense_models.dart';

/// What the editor starts from: an existing expense, a scanned receipt, or
/// nothing for a blank expense.
class ExpenseEditorSeed {
  const ExpenseEditorSeed({
    this.expense,
    this.scan,
    this.receiptBytes,
    this.notice,
  });

  final ExpenseData? expense;
  final ReceiptScan? scan;
  final Uint8List? receiptBytes;

  /// Shown above the form, for example when a receipt could not be read.
  final String? notice;
}

/// Opens the expense form. Returns true when an expense was saved or deleted.
Future<bool> showExpenseEditor(
  BuildContext context, {
  required Dio http,
  ExpenseEditorSeed seed = const ExpenseEditorSeed(),
}) async =>
    await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      useSafeArea: true,
      builder: (_) => _ExpenseEditorSheet(http: http, seed: seed),
    ) ??
    false;

/// Picks a receipt photo, uploads it, lets Jarvis read it, and opens the form
/// with what it found. Returns true when the expense was saved.
Future<bool> scanReceipt(
  BuildContext context, {
  required Dio http,
  ImageSource? source,
}) async {
  final picked = source ?? await _pickSource(context);
  if (picked == null || !context.mounted) return false;
  XFile? photo;
  try {
    photo = await ImagePicker().pickImage(
      source: picked,
      maxWidth: 2048,
      maxHeight: 2048,
      imageQuality: 85,
    );
  } catch (_) {
    if (context.mounted) {
      _snack(
        context,
        picked == ImageSource.camera
            ? 'Jarvis could not open the camera. Check camera access in Settings.'
            : 'Jarvis could not open your photos. Check photo access in Settings.',
      );
    }
    return false;
  }
  if (photo == null || !context.mounted) return false;
  final bytes = await photo.readAsBytes();
  if (!context.mounted) return false;
  if (bytes.isEmpty || bytes.length > 8 * 1024 * 1024) {
    _snack(context, 'That photo is larger than 8 MB.');
    return false;
  }

  final result = await showDialog<ExpenseEditorSeed>(
    context: context,
    barrierDismissible: false,
    builder: (_) => _ScanProgressDialog(
      http: http,
      bytes: bytes,
      fileName: photoFileName(photo!.name),
    ),
  );
  if (result == null || !context.mounted) return false;
  return showExpenseEditor(context, http: http, seed: result);
}

Future<ImageSource?> _pickSource(BuildContext context) =>
    showModalBottomSheet<ImageSource>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const IconBadge(icon: PhosphorIconsRegular.camera),
              title: const Text('Take a photo'),
              subtitle: const Text('Lay the receipt flat in good light'),
              onTap: () => Navigator.pop(sheetContext, ImageSource.camera),
            ),
            ListTile(
              leading: const IconBadge(icon: PhosphorIconsRegular.image),
              title: const Text('Choose from library'),
              onTap: () => Navigator.pop(sheetContext, ImageSource.gallery),
            ),
          ],
        ),
      ),
    );

void _snack(BuildContext context, String message) => ScaffoldMessenger.of(
  context,
).showSnackBar(SnackBar(content: Text(message)));

/// Uploads the photo and asks the server to read it. Pops with the seed for
/// the form, or null when the upload itself failed.
class _ScanProgressDialog extends StatefulWidget {
  const _ScanProgressDialog({
    required this.http,
    required this.bytes,
    required this.fileName,
  });

  final Dio http;
  final Uint8List bytes;
  final String fileName;

  @override
  State<_ScanProgressDialog> createState() => _ScanProgressDialogState();
}

class _ScanProgressDialogState extends State<_ScanProgressDialog> {
  String _step = 'Uploading receipt…';

  @override
  void initState() {
    super.initState();
    unawaited(_run());
  }

  Future<void> _run() async {
    String? fileId;
    try {
      final upload = await widget.http.post<dynamic>(
        '/api/v1/files',
        data: FormData.fromMap({
          'file': MultipartFile.fromBytes(
            widget.bytes,
            filename: widget.fileName,
            contentType: DioMediaType.parse(photoContentType(widget.fileName)),
          ),
        }),
        options: Options(
          sendTimeout: const Duration(minutes: 2),
          receiveTimeout: const Duration(minutes: 2),
        ),
      );
      fileId = jsonId(jsonObject(upload.data));
      if (fileId == null) throw const FormatException('Missing file id.');
    } catch (_) {
      if (!mounted) return;
      _snack(context, 'The receipt could not be uploaded. Try again.');
      Navigator.of(context).pop();
      return;
    }

    if (mounted) setState(() => _step = 'Reading receipt…');
    ExpenseEditorSeed seed;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/expenses/scan',
        data: {'fileId': fileId},
        options: Options(receiveTimeout: const Duration(seconds: 90)),
      );
      seed = ExpenseEditorSeed(
        scan:
            ReceiptScan.fromJson(response.data) ?? ReceiptScan(fileId: fileId),
        receiptBytes: widget.bytes,
      );
    } on DioException catch (error) {
      seed = ExpenseEditorSeed(
        scan: ReceiptScan(fileId: fileId),
        receiptBytes: widget.bytes,
        notice:
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not read this receipt. Fill it in yourself.',
      );
    }
    if (mounted) Navigator.of(context).pop(seed);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    content: Row(
      children: [
        const SizedBox.square(
          dimension: 22,
          child: CircularProgressIndicator(strokeWidth: 2.4),
        ),
        const SizedBox(width: 18),
        Expanded(
          child: Text(
            _step,
            key: const Key('expense-scan-step'),
            style: Theme.of(context).textTheme.titleMedium,
          ),
        ),
      ],
    ),
  );
}

class _ExpenseEditorSheet extends StatefulWidget {
  const _ExpenseEditorSheet({required this.http, required this.seed});

  final Dio http;
  final ExpenseEditorSeed seed;

  @override
  State<_ExpenseEditorSheet> createState() => _ExpenseEditorSheetState();
}

class _ExpenseEditorSheetState extends State<_ExpenseEditorSheet> {
  late final ExpenseData? _existing = widget.seed.expense;
  late final ReceiptScan? _scan = widget.seed.scan;
  late final _amount = TextEditingController(text: _initialAmount());
  late final _merchant = TextEditingController(
    text: _existing?.merchant ?? _scan?.merchant ?? '',
  );
  late final _note = TextEditingController(
    text: _existing?.note ?? _scan?.note ?? '',
  );
  late String? _category = _existing?.category ?? _scan?.category;
  late DateTime _date = _existing?.spentOn ?? _scan?.spentOn ?? _today();
  late final String? _currency = _existing?.currency ?? _scan?.currency;
  late final String? _receiptId = _existing?.receiptFileId ?? _scan?.fileId;
  late Uint8List? _receiptBytes = widget.seed.receiptBytes;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    if (_receiptBytes == null && _receiptId != null) {
      unawaited(_loadReceipt(_receiptId));
    }
  }

  Future<void> _loadReceipt(String fileId) async {
    try {
      final response = await widget.http.get<List<int>>(
        '/api/v1/files/$fileId/content',
        options: Options(responseType: ResponseType.bytes),
      );
      final data = response.data;
      if (data != null && data.isNotEmpty && mounted) {
        setState(() => _receiptBytes = Uint8List.fromList(data));
      }
    } catch (_) {
      // The photo may have been deleted from Files; the expense stays.
    }
  }

  static DateTime _today() {
    final now = DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  String _initialAmount() {
    final amount = _existing?.amount ?? _scan?.amount;
    return amount == null ? '' : amount.toStringAsFixed(2);
  }

  @override
  void dispose() {
    _amount.dispose();
    _merchant.dispose();
    _note.dispose();
    super.dispose();
  }

  double? get _parsedAmount {
    final text = _amount.text.trim().replaceAll(' ', '');
    if (text.isEmpty) return null;
    // Accept "12,50" as well as "12.50".
    final normalized = text.contains(',') && !text.contains('.')
        ? text.replaceAll(',', '.')
        : text.replaceAll(',', '');
    final value = double.tryParse(normalized);
    return value == null || value <= 0 ? null : value;
  }

  Future<void> _pickDate() async {
    final today = _today();
    final picked = await showDatePicker(
      context: context,
      initialDate: _date.isAfter(today) ? today : _date,
      firstDate: DateTime(today.year - 5, today.month, today.day),
      lastDate: today,
    );
    if (picked != null && mounted) setState(() => _date = picked);
  }

  Future<void> _save() async {
    final amount = _parsedAmount;
    if (amount == null) {
      setState(() => _error = 'Enter an amount, for example 12.50.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = <String, Object?>{
      'amount': amount,
      'currency': ?_currency,
      'merchant': _merchant.text.trim(),
      'category': ?_category,
      'note': _note.text.trim(),
      'spentOn': dateKey(_date),
      'receiptFileId': ?_receiptId,
    };
    try {
      if (_existing case final existing?) {
        await widget.http.put<dynamic>(
          '/api/v1/expenses/${existing.id}',
          data: body,
        );
      } else {
        await widget.http.post<dynamic>('/api/v1/expenses', data: body);
      }
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save the expense.';
      });
    }
  }

  Future<void> _delete() async {
    final existing = _existing;
    if (existing == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this expense?',
      message:
          '${formatMoney(existing.amount, existing.currency)} '
          '${existing.merchant == null ? '' : 'at ${existing.merchant} '}'
          'is removed from your overview.',
      confirmLabel: 'Delete',
      destructive: true,
    );
    if (!confirmed || !mounted) return;
    setState(() => _saving = true);
    try {
      await widget.http.delete<dynamic>('/api/v1/expenses/${existing.id}');
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not delete the expense.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final currency = _currency ?? 'EUR';
    final symbol = formatMoney(0, currency).replaceAll(RegExp(r'[\d.,\s]'), '');
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    _existing == null ? 'New expense' : 'Edit expense',
                    style: theme.textTheme.titleLarge,
                  ),
                ),
                if (_existing != null)
                  IconButton(
                    key: const Key('expense-delete'),
                    tooltip: 'Delete expense',
                    onPressed: _saving ? null : () => unawaited(_delete()),
                    icon: Icon(
                      PhosphorIconsRegular.trash,
                      color: colors.danger,
                    ),
                  ),
              ],
            ),
            if (widget.seed.notice case final notice?) ...[
              const SizedBox(height: 10),
              InlineNotice(message: notice),
            ],
            if (_scan != null && widget.seed.notice == null) ...[
              const SizedBox(height: 6),
              Text(
                'Jarvis read this from your receipt. Check it before saving.',
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
            ],
            const SizedBox(height: 18),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: TextField(
                    key: const Key('expense-amount'),
                    controller: _amount,
                    autofocus: _existing == null && _scan?.amount == null,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    inputFormatters: [
                      FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]')),
                    ],
                    style: theme.textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                      fontFeatures: const [FontFeature.tabularFigures()],
                    ),
                    decoration: InputDecoration(
                      labelText: 'Amount',
                      prefixText: symbol.isEmpty ? null : '$symbol ',
                      suffixText: symbol.isEmpty ? currency : null,
                      hintText: '0.00',
                    ),
                    onChanged: (_) {
                      if (_error != null) setState(() => _error = null);
                    },
                  ),
                ),
                if (_receiptBytes case final bytes?) ...[
                  const SizedBox(width: 12),
                  _ReceiptThumb(bytes: bytes),
                ],
              ],
            ),
            const SizedBox(height: 14),
            TextField(
              key: const Key('expense-merchant'),
              controller: _merchant,
              maxLength: 80,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(
                labelText: 'Shop or place',
                hintText: 'Albert Heijn, NS, Café de Jaren…',
                counterText: '',
              ),
            ),
            const SizedBox(height: 16),
            Text('Category', style: theme.textTheme.labelLarge),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final category in expenseCategories)
                  ChoiceChip(
                    key: Key('expense-category-$category'),
                    avatar: Icon(
                      expenseCategoryIcon(category),
                      size: 16,
                      color: _category == category
                          ? null
                          : expenseCategoryColor(colors, category),
                    ),
                    label: Text(expenseCategoryLabel(category)),
                    selected: _category == category,
                    showCheckmark: false,
                    onSelected: (selected) =>
                        setState(() => _category = selected ? category : null),
                  ),
              ],
            ),
            if (_category == null) ...[
              const SizedBox(height: 6),
              Text(
                'Leave it empty and Jarvis picks one from the shop.',
                style: TextStyle(fontSize: 12.5, color: colors.muted),
              ),
            ],
            const SizedBox(height: 16),
            Material(
              color: colors.surfaceMuted,
              borderRadius: BorderRadius.circular(JarvisRadii.md),
              child: ListTile(
                key: const Key('expense-date'),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                ),
                leading: Icon(
                  PhosphorIconsRegular.calendarBlank,
                  color: colors.inkSoft,
                ),
                title: Text(dayLabel(_date)),
                trailing: Icon(
                  PhosphorIconsRegular.caretRight,
                  size: 16,
                  color: colors.muted,
                ),
                onTap: () => unawaited(_pickDate()),
              ),
            ),
            const SizedBox(height: 14),
            TextField(
              key: const Key('expense-note'),
              controller: _note,
              maxLength: 200,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Note (optional)',
                hintText: 'Lunch with Sanne',
                counterText: '',
              ),
            ),
            if (_error case final error?) ...[
              const SizedBox(height: 12),
              Text(
                error,
                key: const Key('expense-error'),
                style: TextStyle(color: colors.danger, fontSize: 13.5),
              ),
            ],
            const SizedBox(height: 20),
            FilledButton(
              key: const Key('expense-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              style: FilledButton.styleFrom(minimumSize: const Size(0, 48)),
              child: _saving
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text(_existing == null ? 'Save expense' : 'Save changes'),
            ),
          ],
        ),
      ),
    );
  }
}

class _ReceiptThumb extends StatelessWidget {
  const _ReceiptThumb({required this.bytes});

  final Uint8List bytes;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    image: true,
    label: 'Receipt photo',
    child: GestureDetector(
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute<void>(
          fullscreenDialog: true,
          builder: (_) => PhotoViewerPage(bytes: bytes, title: 'Receipt'),
        ),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        child: Image.memory(
          bytes,
          width: 64,
          height: 64,
          fit: BoxFit.cover,
          cacheWidth: 192,
        ),
      ),
    ),
  );
}
