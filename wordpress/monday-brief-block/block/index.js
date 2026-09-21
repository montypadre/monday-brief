( function ( blocks, element, blockEditor, components, ServerSideRender, i18n ) {
    const el = element.createElement;
    const { InspectorControls, useBlockProps } = blockEditor;
    const { PanelBody, ToggleControl } = components;
    const { __ } = i18n;

    blocks.registerBlockType( 'monday-brief/summary', {
        edit( { attributes, setAttributes } ) {
            return el(
                'div',
                useBlockProps(),
                el(
                    InspectorControls,
                    null,
                    el(
                        PanelBody,
                        { title: __( 'Display', 'monday-brief-block' ) },
                        el( ToggleControl, {
                            label: __( 'Show headline figures', 'monday-brief-block' ),
                            checked: attributes.showCards,
                            onChange: ( value ) => setAttributes( { showCards: value } ),
                            __nextHasNoMarginBottom: true,
                        } )
                    )
                ),
                el( ServerSideRender, { block: 'monday-brief/summary', attributes } )
            );
        },

        // Dynamic block: the markup comes from render.php on every request.
        save: () => null,
    } );
} )(
    window.wp.blocks,
    window.wp.element,
    window.wp.blockEditor,
    window.wp.components,
    window.wp.serverSideRender,
    window.wp.i18n
);