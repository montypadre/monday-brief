<?php
/**
 * Server-side render for the Monday Brief block.
 *
 * @package MondayBriefBlock
 *
 * @var array       $attributes Block attributes.
 * @var string      $content    Inner content (unused).
 * @var WP_Block    $block      Block instance.
 */

defined( 'ABSPATH' ) || exit;

$result     = mbb_fetch_summary();
$show_cards = ! isset( $attributes['showCards'] ) || (bool) $attributes['showCards'];
$can_edit   = current_user_can( 'edit_posts' );

// Visitors can see nothing rather than an error. Editors see why.
if ( ! $result['ok'] ) {
	if ( $can_edit ) {
		printf(
			'<div %s><p class="monday-brief__notice">$s</p></div>',
			get_block_wrapper_attributes(), // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped -- escaped by core.
			esc_html( (string) $result['error'] )
		);
	}
	return;
}

$data       = $result['data'];
$paragraphs = array_values( array_filter( array_map( 'trim', explode( "\n", (string) ( $data['text'] ?? '' ) ) ) ) );
$cards      = is_array( $data['cards'] ?? null ) ? $data['cards'] : array();
$heading_id = wp_unique_id( 'monday-brief-heading-' );
?>
<section <?php echo get_block_wrapper_attributes( array( 'class' => 'monday-brief' ) ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?> aria-labelledby="<?php echo esc_attr( $heading_id ); ?>">
	<h2 id="<?php echo esc_attr( $heading_id ); ?>" class="monday-brief__heading"><?php esc_html_e( 'This week', 'monday-brief-block' ); ?></h2>

	<?php if ( $paragraphs ) : ?>
		<div class="monday-brief__text">
			<?php foreach ( $paragraphs as $paragraph ) : ?>
				<p><?php echo esc_html( $paragraph ); ?></p>
			<?php endforeach; ?>
		</div>
	<?php endif; ?>

	<?php if ( $show_cards && $cards ) : ?>
		<ul class="monday-brief__cards">
			<?php foreach ( $cards as $card ) : ?>
				<?php $delta = mbb_format_delta( $card ); ?>
				<li class="monday-brief__card">
					<span class="monday-brief__label"><?php echo esc_html( (string) ( $card['label'] ?? '' ) ); ?></span>
					<span class="monday-brief__value"><?php echo esc_html( mbb_format_value( $card['value'] ?? 0, (string) ( $card['format'] ?? 'number' ) ) ); ?></span>
					<span class="monday-brief__delta is-<?php echo esc_attr( $delta['direction'] ); ?>"><?php echo esc_html( $delta['text'] ); ?></span>
				</li>
			<?php endforeach; ?>
		</ul>
	<?php endif; ?>

	<?php if ( ! empty( $data['weekStart'] ) ) : ?>
		<p class="monday-brief__meta">
			<?php
			printf(
				/* translators: %s: date the week starts. */
				esc_html__( 'Week of %s', 'monday-brief-block' ),
				esc_html( mbb_format_date( (string) $data['weekStart'] ) )
			);
			?>
		</p>
	<?php endif; ?>

	<?php if ( $result['stale'] && $can_edit ) : ?>
		<p class="monday-brief__notice">
			<?php
			printf(
				/* translators: %s: why the live fetch failed. */
				esc_html__( 'Showing the last saved copy. %s', 'monday-brief-block' ),
				esc_html( (string) $result['error'] )
			);
			?>
		</p>
	<?php endif; ?>
</section>