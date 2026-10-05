import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../theme/app_colors.dart';
import 'app_button.dart';

/// Data class representing captured evidence photo.
class EvidencePhoto {
  const EvidencePhoto({
    required this.fileName,
    required this.dataUrl,
    required this.bytes,
    required this.contentType,
    required this.sizeBytes,
  });

  final String fileName;
  final String dataUrl;
  final Uint8List bytes;
  final String contentType;
  final int sizeBytes;

  Map<String, dynamic> toPayload() => {
        'fileName': fileName,
        'fileUrl': dataUrl,
        'contentType': contentType,
        'fileSizeBytes': sizeBytes,
      };
}

/// A clean, mobile-optimized evidence capture control supporting native camera
/// capture, gallery selection, live preview thumbnail, and removal.
class EvidencePickerWidget extends StatefulWidget {
  const EvidencePickerWidget({
    super.key,
    required this.onChanged,
    this.title = 'Photo Evidence',
    this.subtitle = 'Capture with camera or choose from gallery',
    this.initialPhoto,
  });

  final ValueChanged<EvidencePhoto?> onChanged;
  final String title;
  final String subtitle;
  final EvidencePhoto? initialPhoto;

  @override
  State<EvidencePickerWidget> createState() => _EvidencePickerWidgetState();
}

class _EvidencePickerWidgetState extends State<EvidencePickerWidget> {
  final ImagePicker _picker = ImagePicker();
  EvidencePhoto? _current;
  bool _loading = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _current = widget.initialPhoto;
  }

  Future<void> _pick(ImageSource source) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final XFile? file = await _picker.pickImage(
        source: source,
        maxWidth: 1200,
        maxHeight: 1200,
        imageQuality: 85,
      );
      if (file == null) {
        if (mounted) setState(() => _loading = false);
        return;
      }

      final bytes = await file.readAsBytes();
      if (bytes.length > 10000000) {
        throw Exception('Evidence files must be 10 MB or less.');
      }
      final mime = file.mimeType ?? 'image/jpeg';
      final base64String = base64Encode(bytes);
      final dataUrl = 'data:$mime;base64,$base64String';
      final name = file.name.isNotEmpty
          ? file.name
          : 'evidence-${DateTime.now().millisecondsSinceEpoch}.jpg';

      final photo = EvidencePhoto(
        fileName: name,
        dataUrl: dataUrl,
        bytes: bytes,
        contentType: mime,
        sizeBytes: bytes.length,
      );

      if (mounted) {
        setState(() {
          _current = photo;
          _loading = false;
        });
        widget.onChanged(photo);
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = 'Could not capture image: ${e.toString().replaceFirst('Exception: ', '')}';
          _loading = false;
        });
      }
    }
  }

  void _remove() {
    setState(() {
      _current = null;
      _error = null;
    });
    widget.onChanged(null);
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(widget.title, style: Theme.of(context).textTheme.titleSmall),
            if (_current != null)
              Text(
                '${(_current!.sizeBytes / 1024).toStringAsFixed(1)} KB',
                style: Theme.of(context).textTheme.labelSmall,
              ),
          ],
        ),
        const SizedBox(height: 4),
        Text(widget.subtitle, style: Theme.of(context).textTheme.bodySmall),
        const SizedBox(height: 10),
        if (_loading)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: Center(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
                  SizedBox(width: 10),
                  Text('Processing image…'),
                ],
              ),
            ),
          )
        else if (_current != null)
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              border: Border.all(color: AppColors.border),
              borderRadius: BorderRadius.circular(8),
              color: AppColors.surfaceMuted,
            ),
            child: Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(6),
                  child: Image.memory(
                    _current!.bytes,
                    width: 64,
                    height: 64,
                    fit: BoxFit.cover,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        _current!.fileName,
                        style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      const SizedBox(height: 4),
                      const Text(
                        'Photo attached & ready for upload',
                        style: TextStyle(color: AppColors.success, fontSize: 11),
                      ),
                    ],
                  ),
                ),
                IconButton(
                  icon: const Icon(Icons.close, size: 20, color: AppColors.danger),
                  tooltip: 'Remove photo',
                  onPressed: _remove,
                ),
              ],
            ),
          )
        else
          Row(
            children: [
              Expanded(
                child: AppButton(
                  label: '📷 Take Photo',
                  variant: AppButtonVariant.secondary,
                  onPressed: () => _pick(ImageSource.camera),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: AppButton(
                  label: '📁 Gallery',
                  variant: AppButtonVariant.secondary,
                  onPressed: () => _pick(ImageSource.gallery),
                ),
              ),
            ],
          ),
        if (_error != null) ...[
          const SizedBox(height: 6),
          Text(_error!, style: const TextStyle(color: AppColors.danger, fontSize: 12)),
        ],
      ],
    );
  }
}
