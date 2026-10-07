import 'package:flutter/material.dart';

/// 单词卡动态背景记忆度进度条
/// - 优雅填充整个单词卡背景区域
/// - 不截获点击事件 (IgnorePointer)
/// - 带有平滑展开动画效果
/// - 在进度条 Leading Edge (右端边缘处) 优雅呈现百分比水印
class WordCardProgressBackground extends StatelessWidget {
  final int retentionPercent; // 0 ~ 100

  const WordCardProgressBackground({
    super.key,
    required this.retentionPercent,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isDark = theme.brightness == Brightness.dark;
    final ratio = (retentionPercent.clamp(0, 100)) / 100.0;

    // 进度背景填充色 (单色黑白极简风格)
    final fillColor = isDark
        ? Colors.white.withValues(alpha: retentionPercent >= 80 ? 0.08 : 0.05)
        : Colors.black.withValues(alpha: retentionPercent >= 80 ? 0.06 : 0.035);

    // Leading edge 竖向边界微光线颜色
    final edgeLineColor = isDark
        ? Colors.white.withValues(alpha: 0.22)
        : Colors.black.withValues(alpha: 0.16);

    // 百分比文字颜色 (同层水印效果，完全不影响上方文字清晰度)
    final percentTextColor = isDark
        ? Colors.white.withValues(alpha: 0.28)
        : Colors.black.withValues(alpha: 0.22);

    return Positioned.fill(
      child: IgnorePointer(
        child: ClipRRect(
          borderRadius: BorderRadius.circular(10),
          child: LayoutBuilder(
            builder: (context, constraints) {
              final maxWidth = constraints.maxWidth;

              return TweenAnimationBuilder<double>(
                tween: Tween<double>(begin: 0.0, end: ratio),
                duration: const Duration(milliseconds: 550),
                curve: Curves.easeOutCubic,
                builder: (context, animRatio, _) {
                  final currentWidth = maxWidth * animRatio;

                  return Stack(
                    children: [
                      // 进度填充背景层
                      if (animRatio > 0)
                        Container(
                          width: currentWidth,
                          height: double.infinity,
                          color: fillColor,
                        ),

                      // Leading Edge 边界指示线与百分比
                      if (retentionPercent > 0)
                        Positioned(
                          left: (currentWidth - 1.5).clamp(0.0, maxWidth - 1.5),
                          top: 0,
                          bottom: 0,
                          child: Container(
                            width: 1.5,
                            color: edgeLineColor,
                          ),
                        ),

                      // 在 Leading Edge 边缘优雅显示百分比 (内嵌同层水印)
                      if (retentionPercent > 0)
                        Positioned(
                          // 若宽度大于 42px 则排在 Leading Edge 左内侧，否则紧随其右
                          left: currentWidth >= 42
                              ? (currentWidth - 38).clamp(4.0, maxWidth - 38)
                              : (currentWidth + 4).clamp(0.0, maxWidth - 38),
                          bottom: 6,
                          child: Text(
                            '$retentionPercent%',
                            style: TextStyle(
                              fontSize: 10,
                              fontWeight: FontWeight.w800,
                              letterSpacing: 0.5,
                              color: percentTextColor,
                              fontFamily: 'monospace',
                            ),
                          ),
                        ),
                    ],
                  );
                },
              );
            },
          ),
        ),
      ),
    );
  }
}
