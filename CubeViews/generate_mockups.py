"""
OneStream CubeView Mockup Generator
Creates realistic illustrations of what each CubeView looks like in the OneStream UI.
"""

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.patches import FancyBboxPatch
import numpy as np
import os

OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Mockups")
os.makedirs(OUTPUT_DIR, exist_ok=True)

# Brand colors
NAVY = '#0B1D3A'
HEADER_BG = '#1A3A5C'
ROW_LIGHT = '#FFFFFF'
ROW_ALT = '#F0F4F8'
ACCENT_BLUE = '#006EC7'
LIGHT_BLUE = '#E8F1FA'
GRID_LINE = '#D0D5DD'
INPUT_CELL_BG = '#FFFDE7'
CALC_CELL_BG = '#F0F4F8'
NEGATIVE_RED = '#DC3545'
POSITIVE_GREEN = '#28A745'
TOOLBAR_BG = '#2C3E50'
POV_BG = '#34495E'
SECTION_BG = '#E3EBF3'
BOLD_ROW_BG = '#D6E4F0'


def fmt_num(val, decimals=0, prefix='', suffix='', parens_neg=True):
    if val is None:
        return ''
    if decimals == 0:
        s = f"{abs(val):,.0f}"
    else:
        s = f"{abs(val):,.{decimals}f}"
    if val < 0:
        if parens_neg:
            return f"{prefix}({s}){suffix}"
        else:
            return f"-{prefix}{s}{suffix}"
    return f"{prefix}{s}{suffix}"


def fmt_pct(val):
    if val is None:
        return ''
    return f"{val:.1f}%"


def draw_cubeview(fig_width, fig_height, title, subtitle, pov_items, col_headers,
                  rows, col_widths=None, input_cols=None, save_name='mockup',
                  show_variance_colors=False, variance_col_indices=None):
    """
    rows: list of dicts with keys:
        'indent': int, 'label': str, 'values': list, 'bold': bool,
        'section': bool (section header), 'separator': bool, 'level': int
    """
    fig, ax = plt.subplots(1, 1, figsize=(fig_width, fig_height))
    ax.set_xlim(0, fig_width)
    ax.set_ylim(0, fig_height)
    ax.axis('off')
    fig.patch.set_facecolor('#EAECF0')

    y_cursor = fig_height

    # --- Title Bar ---
    title_h = 0.55
    y_cursor -= title_h
    rect = FancyBboxPatch((0.1, y_cursor), fig_width - 0.2, title_h,
                           boxstyle="round,pad=0.02", facecolor=NAVY, edgecolor='none')
    ax.add_patch(rect)
    ax.text(0.35, y_cursor + title_h * 0.62, title,
            fontsize=13, fontweight='bold', color='white', va='center', fontfamily='sans-serif')
    ax.text(0.35, y_cursor + title_h * 0.25, subtitle,
            fontsize=8, color='#8899AA', va='center', fontfamily='sans-serif')

    # OneStream logo placeholder
    ax.text(fig_width - 0.35, y_cursor + title_h * 0.5, "OneStream",
            fontsize=7, color='#4DA8DA', va='center', ha='right', fontfamily='sans-serif',
            fontweight='bold')

    y_cursor -= 0.08

    # --- POV Bar ---
    pov_h = 0.38
    y_cursor -= pov_h
    rect = FancyBboxPatch((0.1, y_cursor), fig_width - 0.2, pov_h,
                           boxstyle="round,pad=0.02", facecolor=POV_BG, edgecolor='none')
    ax.add_patch(rect)

    pov_x = 0.25
    for label, value in pov_items:
        ax.text(pov_x, y_cursor + pov_h * 0.6, label + ":",
                fontsize=6.5, color='#8899AA', va='center', fontfamily='sans-serif')
        ax.text(pov_x, y_cursor + pov_h * 0.25, value,
                fontsize=7, color='white', va='center', fontfamily='sans-serif', fontweight='bold')
        pov_x += (fig_width - 0.5) / len(pov_items)

    y_cursor -= 0.08

    # --- Toolbar ---
    tb_h = 0.28
    y_cursor -= tb_h
    rect = FancyBboxPatch((0.1, y_cursor), fig_width - 0.2, tb_h,
                           boxstyle="round,pad=0.01", facecolor='#F8F9FA', edgecolor=GRID_LINE)
    ax.add_patch(rect)

    tools = ['Save', 'Submit', 'Calculate', 'Refresh', 'Export', 'Suppress Zeros']
    tx = 0.3
    for tool in tools:
        btn = FancyBboxPatch((tx, y_cursor + 0.04), len(tool) * 0.065 + 0.12, 0.2,
                              boxstyle="round,pad=0.02", facecolor='white', edgecolor=GRID_LINE,
                              linewidth=0.5)
        ax.add_patch(btn)
        ax.text(tx + 0.06 + len(tool) * 0.0325, y_cursor + tb_h * 0.5, tool,
                fontsize=6, color='#495057', va='center', ha='center', fontfamily='sans-serif')
        tx += len(tool) * 0.065 + 0.22

    y_cursor -= 0.06

    # --- Grid ---
    n_cols = len(col_headers)
    total_data_rows = len(rows)
    row_h = 0.28
    header_h = 0.32

    if col_widths is None:
        label_w = 2.8
        data_w = (fig_width - 0.2 - label_w) / n_cols
        col_widths_px = [label_w] + [data_w] * n_cols
    else:
        col_widths_px = col_widths

    grid_w = sum(col_widths_px)
    grid_x = 0.1

    # Column headers
    y_cursor -= header_h
    x = grid_x
    # Row label header
    rect = FancyBboxPatch((x, y_cursor), col_widths_px[0], header_h,
                           boxstyle="square,pad=0", facecolor=HEADER_BG, edgecolor=GRID_LINE,
                           linewidth=0.5)
    ax.add_patch(rect)
    ax.text(x + 0.1, y_cursor + header_h / 2, "Account",
            fontsize=7.5, fontweight='bold', color='white', va='center', fontfamily='sans-serif')
    x += col_widths_px[0]

    for i, header in enumerate(col_headers):
        w = col_widths_px[i + 1] if i + 1 < len(col_widths_px) else col_widths_px[-1]
        is_input = input_cols and i in input_cols
        bg = HEADER_BG
        rect = FancyBboxPatch((x, y_cursor), w, header_h,
                               boxstyle="square,pad=0", facecolor=bg, edgecolor=GRID_LINE,
                               linewidth=0.5)
        ax.add_patch(rect)
        ax.text(x + w / 2, y_cursor + header_h / 2, header,
                fontsize=7, fontweight='bold', color='white', va='center', ha='center',
                fontfamily='sans-serif')
        x += w

    # Data rows
    for r_idx, row in enumerate(rows):
        y_cursor -= row_h
        x = grid_x

        is_bold = row.get('bold', False)
        is_section = row.get('section', False)
        is_separator = row.get('separator', False)
        indent = row.get('indent', 0)
        level = row.get('level', 0)

        if is_separator:
            ax.plot([grid_x, grid_x + grid_w], [y_cursor + row_h, y_cursor + row_h],
                    color=ACCENT_BLUE, linewidth=1.5)
            continue

        if is_section:
            bg = SECTION_BG
        elif is_bold:
            bg = BOLD_ROW_BG
        elif r_idx % 2 == 0:
            bg = ROW_LIGHT
        else:
            bg = ROW_ALT

        # Row label cell
        rect = FancyBboxPatch((x, y_cursor), col_widths_px[0], row_h,
                               boxstyle="square,pad=0", facecolor=bg, edgecolor=GRID_LINE,
                               linewidth=0.3)
        ax.add_patch(rect)

        label_text = row.get('label', '')
        label_x = x + 0.1 + indent * 0.2
        ax.text(label_x, y_cursor + row_h / 2, label_text,
                fontsize=7 if not is_section else 7.5,
                fontweight='bold' if (is_bold or is_section) else 'normal',
                color=NAVY if (is_bold or is_section) else '#343A40',
                va='center', fontfamily='sans-serif')
        x += col_widths_px[0]

        # Value cells
        values = row.get('values', [])
        for c_idx, val in enumerate(values):
            w = col_widths_px[c_idx + 1] if c_idx + 1 < len(col_widths_px) else col_widths_px[-1]
            is_input = input_cols and c_idx in input_cols and not is_bold and not is_section

            if is_input:
                cell_bg = INPUT_CELL_BG
            elif is_section:
                cell_bg = SECTION_BG
            elif is_bold:
                cell_bg = BOLD_ROW_BG
            else:
                cell_bg = bg

            rect = FancyBboxPatch((x, y_cursor), w, row_h,
                                   boxstyle="square,pad=0", facecolor=cell_bg, edgecolor=GRID_LINE,
                                   linewidth=0.3)
            ax.add_patch(rect)

            if is_input and not is_bold and not is_section:
                input_indicator = FancyBboxPatch((x + 0.02, y_cursor + 0.02), w - 0.04, row_h - 0.04,
                                                  boxstyle="round,pad=0.01", facecolor='none',
                                                  edgecolor='#FFC107', linewidth=0.5)
                ax.add_patch(input_indicator)

            display_val = val if isinstance(val, str) else str(val)

            # Color logic
            txt_color = '#343A40'
            if show_variance_colors and variance_col_indices and c_idx in variance_col_indices:
                if isinstance(val, str) and val.startswith('('):
                    txt_color = NEGATIVE_RED
                elif isinstance(val, str) and val.startswith('-'):
                    txt_color = NEGATIVE_RED
                elif isinstance(val, str) and '%' in val and val.replace('%', '').replace('.', '').replace('-', '').strip().isdigit():
                    try:
                        pct_val = float(val.replace('%', '').strip())
                        if pct_val < 0:
                            txt_color = NEGATIVE_RED
                        elif pct_val > 0:
                            txt_color = POSITIVE_GREEN
                    except:
                        pass

            if is_bold or is_section:
                fw = 'bold'
            else:
                fw = 'normal'

            ax.text(x + w - 0.08, y_cursor + row_h / 2, display_val,
                    fontsize=6.5, fontweight=fw, color=txt_color,
                    va='center', ha='right', fontfamily='sans-serif')
            x += w

    # Border
    total_h = fig_height - y_cursor - 0.1
    rect = FancyBboxPatch((0.08, y_cursor - 0.02), grid_w + 0.04, total_h,
                           boxstyle="round,pad=0.02", facecolor='none', edgecolor=GRID_LINE,
                           linewidth=1)
    ax.add_patch(rect)

    plt.tight_layout(pad=0.1)
    save_path = os.path.join(OUTPUT_DIR, f"{save_name}.png")
    fig.savefig(save_path, dpi=180, bbox_inches='tight', facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Saved: {save_path}")
    return save_path


# ==============================================================================
# 1. CV_DataEntry_Revenue
# ==============================================================================
def create_revenue_entry():
    print("Creating Revenue Data Entry...")
    months = ['Jan 2026', 'Feb 2026', 'Mar 2026', 'Q1 Total', 'Apr 2026', 'May 2026', 'Jun 2026', 'Q2 Total']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Budget 2026'),
           ('Product', 'All Products'), ('Version', 'Working')]

    rows = [
        {'label': 'Gross Revenue', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '', '', '']},
        {'label': 'Domestic Revenue', 'indent': 1, 'values': ['4,250,000', '4,180,000', '4,520,000', '12,950,000', '4,680,000', '4,750,000', '4,890,000', '14,320,000']},
        {'label': 'Export Revenue', 'indent': 1, 'values': ['1,850,000', '1,790,000', '2,010,000', '5,650,000', '2,120,000', '2,080,000', '2,250,000', '6,450,000']},
        {'label': 'Intercompany Revenue', 'indent': 1, 'values': ['980,000', '1,020,000', '1,050,000', '3,050,000', '1,100,000', '1,080,000', '1,150,000', '3,330,000']},
        {'label': 'Total Gross Revenue', 'indent': 0, 'bold': True, 'values': ['7,080,000', '6,990,000', '7,580,000', '21,650,000', '7,900,000', '7,910,000', '8,290,000', '24,100,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Revenue Deductions', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '', '', '']},
        {'label': 'Discounts', 'indent': 1, 'values': ['(142,000)', '(140,000)', '(152,000)', '(434,000)', '(158,000)', '(158,000)', '(166,000)', '(482,000)']},
        {'label': 'Returns & Allowances', 'indent': 1, 'values': ['(71,000)', '(70,000)', '(76,000)', '(217,000)', '(79,000)', '(79,000)', '(83,000)', '(241,000)']},
        {'label': 'Rebates', 'indent': 1, 'values': ['(106,000)', '(105,000)', '(114,000)', '(325,000)', '(119,000)', '(119,000)', '(124,000)', '(362,000)']},
        {'label': 'Total Deductions', 'indent': 0, 'bold': True, 'values': ['(319,000)', '(315,000)', '(342,000)', '(976,000)', '(356,000)', '(356,000)', '(373,000)', '(1,085,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Net Revenue', 'indent': 0, 'bold': True, 'values': ['6,761,000', '6,675,000', '7,238,000', '20,674,000', '7,544,000', '7,554,000', '7,917,000', '23,015,000']},
    ]

    draw_cubeview(14, 6.5, 'Revenue Data Entry', 'CubeView: CV_DataEntry_Revenue',
                  pov, months, rows, input_cols={0, 1, 2, 4, 5, 6},
                  save_name='CV_DataEntry_Revenue')


# ==============================================================================
# 2. CV_DataEntry_OPEX
# ==============================================================================
def create_opex_entry():
    print("Creating OPEX Data Entry...")
    months = ['Jan 2026', 'Feb 2026', 'Mar 2026', 'Q1 Total', 'FY Budget', 'FY Prior']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Budget 2026'),
           ('Cost Center', 'All Centers'), ('Version', 'Working')]

    rows = [
        {'label': 'Selling, General & Administrative', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Salaries & Wages', 'indent': 1, 'values': ['285,000', '285,000', '285,000', '855,000', '3,420,000', '3,280,000']},
        {'label': 'Benefits & Insurance', 'indent': 1, 'values': ['85,500', '85,500', '85,500', '256,500', '1,026,000', '984,000']},
        {'label': 'Travel & Entertainment', 'indent': 1, 'values': ['42,000', '38,000', '45,000', '125,000', '520,000', '480,000']},
        {'label': 'Professional Services', 'indent': 1, 'values': ['65,000', '55,000', '70,000', '190,000', '780,000', '720,000']},
        {'label': 'Office & Supplies', 'indent': 1, 'values': ['18,000', '16,000', '19,000', '53,000', '210,000', '195,000']},
        {'label': 'Total SG&A', 'indent': 0, 'bold': True, 'values': ['495,500', '479,500', '504,500', '1,479,500', '5,956,000', '5,659,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Research & Development', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'R&D Salaries', 'indent': 1, 'values': ['180,000', '180,000', '180,000', '540,000', '2,160,000', '2,040,000']},
        {'label': 'Lab Materials', 'indent': 1, 'values': ['35,000', '42,000', '38,000', '115,000', '460,000', '410,000']},
        {'label': 'Prototype Costs', 'indent': 1, 'values': ['25,000', '30,000', '28,000', '83,000', '350,000', '300,000']},
        {'label': 'Total R&D', 'indent': 0, 'bold': True, 'values': ['240,000', '252,000', '246,000', '738,000', '2,970,000', '2,750,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Marketing', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Digital Marketing', 'indent': 1, 'values': ['45,000', '48,000', '52,000', '145,000', '600,000', '520,000']},
        {'label': 'Trade Shows & Events', 'indent': 1, 'values': ['20,000', '15,000', '65,000', '100,000', '380,000', '350,000']},
        {'label': 'Total Marketing', 'indent': 0, 'bold': True, 'values': ['65,000', '63,000', '117,000', '245,000', '980,000', '870,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Total Operating Expenses', 'indent': 0, 'bold': True, 'values': ['800,500', '794,500', '867,500', '2,462,500', '9,906,000', '9,279,000']},
    ]

    draw_cubeview(13, 8, 'Operating Expense Data Entry', 'CubeView: CV_DataEntry_OPEX',
                  pov, months, rows, input_cols={0, 1, 2},
                  save_name='CV_DataEntry_OPEX')


# ==============================================================================
# 3. CV_DataEntry_Headcount
# ==============================================================================
def create_headcount_entry():
    print("Creating Headcount Data Entry...")
    cols = ['FTE Count', 'Avg Base Salary', 'Benefits Rate', 'Total Comp', 'Annual Cost', 'vs Prior']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Budget 2026'),
           ('Time', 'FY2026'), ('Version', 'Working')]

    rows = [
        {'label': 'Production', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Assembly Line Operators', 'indent': 1, 'values': ['85', '$52,000', '32.5%', '$68,900', '$5,856,500', '+3 FTE']},
        {'label': 'Machine Operators', 'indent': 1, 'values': ['42', '$58,000', '32.5%', '$76,850', '$3,227,700', '+2 FTE']},
        {'label': 'Quality Inspectors', 'indent': 1, 'values': ['18', '$55,000', '32.5%', '$72,875', '$1,311,750', '0']},
        {'label': 'Production Supervisors', 'indent': 1, 'values': ['8', '$78,000', '30.0%', '$101,400', '$811,200', '0']},
        {'label': 'Maintenance Technicians', 'indent': 1, 'values': ['12', '$62,000', '32.5%', '$82,150', '$985,800', '+1 FTE']},
        {'label': 'Total Production', 'indent': 0, 'bold': True, 'values': ['165', '$56,800', '32.1%', '$75,243', '$12,192,950', '+6 FTE']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Engineering', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Manufacturing Engineers', 'indent': 1, 'values': ['14', '$92,000', '30.0%', '$119,600', '$1,674,400', '+2 FTE']},
        {'label': 'Process Engineers', 'indent': 1, 'values': ['8', '$88,000', '30.0%', '$114,400', '$915,200', '+1 FTE']},
        {'label': 'Design Engineers', 'indent': 1, 'values': ['6', '$95,000', '30.0%', '$123,500', '$741,000', '0']},
        {'label': 'Total Engineering', 'indent': 0, 'bold': True, 'values': ['28', '$91,429', '30.0%', '$118,957', '$3,330,600', '+3 FTE']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Administration', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Finance & Accounting', 'indent': 1, 'values': ['6', '$75,000', '30.0%', '$97,500', '$585,000', '0']},
        {'label': 'HR & Admin', 'indent': 1, 'values': ['4', '$68,000', '30.0%', '$88,400', '$353,600', '0']},
        {'label': 'Plant Management', 'indent': 1, 'values': ['3', '$125,000', '28.0%', '$160,000', '$480,000', '0']},
        {'label': 'Total Administration', 'indent': 0, 'bold': True, 'values': ['13', '$84,615', '29.5%', '$109,046', '$1,418,600', '0']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Total Plant Headcount', 'indent': 0, 'bold': True, 'values': ['206', '$65,250', '31.5%', '$82,243', '$16,942,150', '+9 FTE']},
    ]

    cw = [2.8, 1.2, 1.5, 1.3, 1.3, 1.5, 1.2]
    draw_cubeview(12.5, 8.5, 'Headcount Planning', 'CubeView: CV_DataEntry_Headcount',
                  pov, cols, rows, col_widths=cw, input_cols={0, 1, 2},
                  save_name='CV_DataEntry_Headcount')


# ==============================================================================
# 4. CV_DataEntry_CAPEX
# ==============================================================================
def create_capex_entry():
    print("Creating CAPEX Data Entry...")
    cols = ['Total Budget', 'Prior Spend', 'Q1 2026', 'Q2 2026', 'Q3 2026', 'Q4 2026', 'EAC', '% Complete']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Budget 2026'),
           ('Project', 'All Projects'), ('Version', 'Working')]

    rows = [
        {'label': 'Expansion Projects', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '', '', '']},
        {'label': 'New Assembly Line B3', 'indent': 1, 'values': ['8,500,000', '2,100,000', '1,600,000', '1,800,000', '1,500,000', '1,500,000', '8,500,000', '24.7%']},
        {'label': 'Warehouse Expansion Wing C', 'indent': 1, 'values': ['3,200,000', '800,000', '600,000', '700,000', '600,000', '500,000', '3,200,000', '25.0%']},
        {'label': 'Parking & Infrastructure', 'indent': 1, 'values': ['1,100,000', '0', '275,000', '275,000', '275,000', '275,000', '1,100,000', '0.0%']},
        {'label': 'Total Expansion', 'indent': 0, 'bold': True, 'values': ['12,800,000', '2,900,000', '2,475,000', '2,775,000', '2,375,000', '2,275,000', '12,800,000', '22.7%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Automation Projects', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '', '', '']},
        {'label': 'Robotic Welding Cell', 'indent': 1, 'values': ['2,400,000', '600,000', '450,000', '450,000', '450,000', '450,000', '2,400,000', '25.0%']},
        {'label': 'AGV Fleet (12 units)', 'indent': 1, 'values': ['1,800,000', '0', '450,000', '450,000', '450,000', '450,000', '1,800,000', '0.0%']},
        {'label': 'Vision Inspection System', 'indent': 1, 'values': ['950,000', '200,000', '250,000', '250,000', '250,000', '0', '950,000', '21.1%']},
        {'label': 'Total Automation', 'indent': 0, 'bold': True, 'values': ['5,150,000', '800,000', '1,150,000', '1,150,000', '1,150,000', '900,000', '5,150,000', '15.5%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Maintenance CAPEX', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '', '', '']},
        {'label': 'CNC Machine Replacement', 'indent': 1, 'values': ['650,000', '0', '325,000', '325,000', '0', '0', '650,000', '0.0%']},
        {'label': 'HVAC System Upgrade', 'indent': 1, 'values': ['420,000', '100,000', '160,000', '160,000', '0', '0', '420,000', '23.8%']},
        {'label': 'IT Infrastructure Refresh', 'indent': 1, 'values': ['380,000', '0', '95,000', '95,000', '95,000', '95,000', '380,000', '0.0%']},
        {'label': 'Total Maintenance', 'indent': 0, 'bold': True, 'values': ['1,450,000', '100,000', '580,000', '580,000', '95,000', '95,000', '1,450,000', '6.9%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Total Capital Expenditure', 'indent': 0, 'bold': True, 'values': ['19,400,000', '3,800,000', '4,205,000', '4,505,000', '3,620,000', '3,270,000', '19,400,000', '19.6%']},
    ]

    cw = [2.8, 1.3, 1.2, 1.2, 1.2, 1.2, 1.2, 1.3, 1.0]
    draw_cubeview(14, 8, 'Capital Expenditure Planning', 'CubeView: CV_DataEntry_CAPEX',
                  pov, cols, rows, col_widths=cw, input_cols={2, 3, 4, 5},
                  save_name='CV_DataEntry_CAPEX')


# ==============================================================================
# 5. CV_DataEntry_Production
# ==============================================================================
def create_production_entry():
    print("Creating Production Data Entry...")
    cols = ['Jan 2026', 'Feb 2026', 'Mar 2026', 'Q1 Total', 'Q1 Prior', 'Var %']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Budget 2026'),
           ('Product', 'Industrial'), ('Version', 'Working')]

    rows = [
        {'label': 'Production Volume (Units)', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'IND-HYD-001 Hydraulic Pump', 'indent': 1, 'values': ['4,200', '4,100', '4,500', '12,800', '12,200', '4.9%']},
        {'label': 'IND-HYD-002 Hydraulic Valve', 'indent': 1, 'values': ['6,800', '6,500', '7,200', '20,500', '19,800', '3.5%']},
        {'label': 'IND-PNE-001 Pneumatic Cylinder', 'indent': 1, 'values': ['8,500', '8,200', '9,000', '25,700', '24,500', '4.9%']},
        {'label': 'IND-MOT-001 Electric Motor', 'indent': 1, 'values': ['3,100', '3,000', '3,400', '9,500', '9,100', '4.4%']},
        {'label': 'Total Production Volume', 'indent': 0, 'bold': True, 'values': ['22,600', '21,800', '24,100', '68,500', '65,600', '4.4%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Machine Hours', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Available Hours', 'indent': 1, 'values': ['12,480', '11,520', '13,440', '37,440', '37,440', '0.0%']},
        {'label': 'Planned Hours', 'indent': 1, 'values': ['10,600', '9,800', '11,200', '31,600', '30,100', '5.0%']},
        {'label': 'Capacity Utilization', 'indent': 1, 'values': ['84.9%', '85.1%', '83.3%', '84.4%', '80.4%', '4.0%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Quality Metrics', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'First Pass Yield %', 'indent': 1, 'values': ['96.5%', '96.8%', '96.2%', '96.5%', '95.8%', '0.7%']},
        {'label': 'Scrap Rate %', 'indent': 1, 'values': ['2.1%', '1.9%', '2.3%', '2.1%', '2.5%', '-0.4%']},
        {'label': 'OEE %', 'indent': 1, 'values': ['82.1%', '82.8%', '81.5%', '82.1%', '79.8%', '2.3%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Labor Hours', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Regular Hours', 'indent': 1, 'values': ['26,400', '24,640', '27,280', '78,320', '75,680', '3.5%']},
        {'label': 'Overtime Hours', 'indent': 1, 'values': ['1,850', '1,600', '2,100', '5,550', '5,800', '-4.3%']},
        {'label': 'Total Labor Hours', 'indent': 0, 'bold': True, 'values': ['28,250', '26,240', '29,380', '83,870', '81,480', '2.9%']},
    ]

    cw = [2.8, 1.3, 1.3, 1.3, 1.3, 1.3, 1.0]
    draw_cubeview(12, 8.5, 'Production Volume & Capacity', 'CubeView: CV_DataEntry_Production',
                  pov, cols, rows, col_widths=cw, input_cols={0, 1, 2},
                  save_name='CV_DataEntry_Production')


# ==============================================================================
# 6. CV_Report_PL
# ==============================================================================
def create_pl_report():
    print("Creating P&L Report...")
    cols = ['Actual', 'Budget', 'Var $', 'Var %', 'Prior Year', 'YoY %']
    pov = [('Entity', 'Americas (Consolidated)'), ('Scenario', 'Actual'),
           ('Time', 'Q1 2026'), ('Currency', 'USD')]

    rows = [
        {'label': 'Gross Revenue', 'indent': 0, 'values': ['48,250,000', '47,800,000', '450,000', '0.9%', '44,100,000', '9.4%'], 'bold': False},
        {'label': 'Less: Discounts & Returns', 'indent': 1, 'values': ['(2,412,500)', '(2,390,000)', '(22,500)', '0.9%', '(2,205,000)', '9.4%']},
        {'label': 'Net Revenue', 'indent': 0, 'bold': True, 'values': ['45,837,500', '45,410,000', '427,500', '0.9%', '41,895,000', '9.4%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Cost of Goods Sold', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Direct Materials', 'indent': 1, 'values': ['(15,800,000)', '(15,950,000)', '150,000', '-0.9%', '(14,600,000)', '8.2%']},
        {'label': 'Direct Labor', 'indent': 1, 'values': ['(7,250,000)', '(7,180,000)', '(70,000)', '1.0%', '(6,700,000)', '8.2%']},
        {'label': 'Manufacturing Overhead', 'indent': 1, 'values': ['(5,100,000)', '(5,200,000)', '100,000', '-1.9%', '(4,800,000)', '6.3%']},
        {'label': 'Cost Variances', 'indent': 1, 'values': ['(320,000)', '0', '(320,000)', 'N/A', '(280,000)', '14.3%']},
        {'label': 'Total COGS', 'indent': 0, 'bold': True, 'values': ['(28,470,000)', '(28,330,000)', '(140,000)', '0.5%', '(26,380,000)', '7.9%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Gross Profit', 'indent': 0, 'bold': True, 'values': ['17,367,500', '17,080,000', '287,500', '1.7%', '15,515,000', '11.9%']},
        {'label': 'Gross Margin %', 'indent': 1, 'values': ['37.9%', '37.6%', '0.3%', '', '37.0%', '']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Operating Expenses', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Selling, General & Admin', 'indent': 1, 'values': ['(4,800,000)', '(4,950,000)', '150,000', '-3.0%', '(4,500,000)', '6.7%']},
        {'label': 'Research & Development', 'indent': 1, 'values': ['(2,200,000)', '(2,250,000)', '50,000', '-2.2%', '(2,000,000)', '10.0%']},
        {'label': 'Marketing', 'indent': 1, 'values': ['(750,000)', '(780,000)', '30,000', '-3.8%', '(680,000)', '10.3%']},
        {'label': 'Corporate Allocation', 'indent': 1, 'values': ['(1,100,000)', '(1,100,000)', '0', '0.0%', '(1,050,000)', '4.8%']},
        {'label': 'Total OPEX', 'indent': 0, 'bold': True, 'values': ['(8,850,000)', '(9,080,000)', '230,000', '-2.5%', '(8,230,000)', '7.5%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'EBITDA', 'indent': 0, 'bold': True, 'values': ['8,517,500', '8,000,000', '517,500', '6.5%', '7,285,000', '16.9%']},
        {'label': 'EBITDA Margin %', 'indent': 1, 'values': ['18.6%', '17.6%', '1.0%', '', '17.4%', '']},
        {'label': 'Depreciation & Amortization', 'indent': 1, 'values': ['(1,850,000)', '(1,800,000)', '(50,000)', '2.8%', '(1,700,000)', '8.8%']},
        {'label': 'EBIT', 'indent': 0, 'bold': True, 'values': ['6,667,500', '6,200,000', '467,500', '7.5%', '5,585,000', '19.4%']},
        {'label': 'Interest Expense', 'indent': 1, 'values': ['(450,000)', '(480,000)', '30,000', '-6.3%', '(520,000)', '-13.5%']},
        {'label': 'Other Income/(Expense)', 'indent': 1, 'values': ['85,000', '50,000', '35,000', '70.0%', '60,000', '41.7%']},
        {'label': 'EBT', 'indent': 0, 'bold': True, 'values': ['6,302,500', '5,770,000', '532,500', '9.2%', '5,125,000', '23.0%']},
        {'label': 'Income Tax', 'indent': 1, 'values': ['(1,575,625)', '(1,442,500)', '(133,125)', '9.2%', '(1,281,250)', '23.0%']},
        {'label': 'Net Income', 'indent': 0, 'bold': True, 'values': ['4,726,875', '4,327,500', '399,375', '9.2%', '3,843,750', '23.0%']},
    ]

    cw = [3.2, 1.5, 1.5, 1.3, 0.9, 1.5, 0.9]
    draw_cubeview(13, 11.5, 'Income Statement', 'CubeView: CV_Report_PL  |  Consolidated Americas',
                  pov, cols, rows, col_widths=cw, show_variance_colors=True,
                  variance_col_indices={2, 3, 4, 5}, save_name='CV_Report_PL')


# ==============================================================================
# 7. CV_Report_BS
# ==============================================================================
def create_bs_report():
    print("Creating Balance Sheet Report...")
    cols = ['Current Period', 'Prior Period', 'Change', 'Prior Year', 'YoY Change']
    pov = [('Entity', 'Global (Consolidated)'), ('Scenario', 'Actual'),
           ('Time', 'Mar 2026'), ('Consolidation', 'Consolidated')]

    rows = [
        {'label': 'ASSETS', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Current Assets', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Cash & Equivalents', 'indent': 1, 'values': ['28,500,000', '26,200,000', '2,300,000', '22,800,000', '5,700,000']},
        {'label': 'Accounts Receivable', 'indent': 1, 'values': ['42,100,000', '40,800,000', '1,300,000', '38,500,000', '3,600,000']},
        {'label': 'Raw Materials Inventory', 'indent': 1, 'values': ['18,200,000', '17,800,000', '400,000', '16,500,000', '1,700,000']},
        {'label': 'Work in Progress', 'indent': 1, 'values': ['8,900,000', '9,200,000', '(300,000)', '8,100,000', '800,000']},
        {'label': 'Finished Goods', 'indent': 1, 'values': ['15,600,000', '14,900,000', '700,000', '14,200,000', '1,400,000']},
        {'label': 'Prepaid Expenses', 'indent': 1, 'values': ['3,200,000', '3,400,000', '(200,000)', '2,900,000', '300,000']},
        {'label': 'Total Current Assets', 'indent': 0, 'bold': True, 'values': ['116,500,000', '112,300,000', '4,200,000', '103,000,000', '13,500,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Non-Current Assets', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Property, Plant & Equipment', 'indent': 1, 'values': ['185,000,000', '183,500,000', '1,500,000', '175,000,000', '10,000,000']},
        {'label': 'Less: Accumulated Depreciation', 'indent': 1, 'values': ['(62,000,000)', '(60,150,000)', '(1,850,000)', '(54,800,000)', '(7,200,000)']},
        {'label': 'Net PP&E', 'indent': 0, 'bold': True, 'values': ['123,000,000', '123,350,000', '(350,000)', '120,200,000', '2,800,000']},
        {'label': 'Goodwill', 'indent': 1, 'values': ['45,000,000', '45,000,000', '0', '45,000,000', '0']},
        {'label': 'Intangible Assets', 'indent': 1, 'values': ['12,800,000', '13,100,000', '(300,000)', '14,000,000', '(1,200,000)']},
        {'label': 'Total Non-Current Assets', 'indent': 0, 'bold': True, 'values': ['180,800,000', '181,450,000', '(650,000)', '179,200,000', '1,600,000']},
        {'label': 'TOTAL ASSETS', 'indent': 0, 'bold': True, 'values': ['297,300,000', '293,750,000', '3,550,000', '282,200,000', '15,100,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'LIABILITIES', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Accounts Payable', 'indent': 1, 'values': ['32,500,000', '31,200,000', '1,300,000', '28,900,000', '3,600,000']},
        {'label': 'Accrued Expenses', 'indent': 1, 'values': ['14,800,000', '15,200,000', '(400,000)', '13,500,000', '1,300,000']},
        {'label': 'Short-Term Debt', 'indent': 1, 'values': ['10,000,000', '10,000,000', '0', '12,000,000', '(2,000,000)']},
        {'label': 'Total Current Liabilities', 'indent': 0, 'bold': True, 'values': ['57,300,000', '56,400,000', '900,000', '54,400,000', '2,900,000']},
        {'label': 'Long-Term Debt', 'indent': 1, 'values': ['65,000,000', '65,000,000', '0', '70,000,000', '(5,000,000)']},
        {'label': 'Deferred Tax Liability', 'indent': 1, 'values': ['8,200,000', '8,000,000', '200,000', '7,500,000', '700,000']},
        {'label': 'Pension Obligations', 'indent': 1, 'values': ['12,500,000', '12,600,000', '(100,000)', '12,800,000', '(300,000)']},
        {'label': 'Total Liabilities', 'indent': 0, 'bold': True, 'values': ['143,000,000', '142,000,000', '1,000,000', '144,700,000', '(1,700,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'EQUITY', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Common Stock', 'indent': 1, 'values': ['50,000,000', '50,000,000', '0', '50,000,000', '0']},
        {'label': 'Retained Earnings', 'indent': 1, 'values': ['98,500,000', '96,200,000', '2,300,000', '82,500,000', '16,000,000']},
        {'label': 'Other Comprehensive Income', 'indent': 1, 'values': ['(2,200,000)', '(2,450,000)', '250,000', '(3,000,000)', '800,000']},
        {'label': 'Minority Interest', 'indent': 1, 'values': ['8,000,000', '8,000,000', '0', '8,000,000', '0']},
        {'label': 'Total Equity', 'indent': 0, 'bold': True, 'values': ['154,300,000', '151,750,000', '2,550,000', '137,500,000', '16,800,000']},
        {'label': 'TOTAL LIABILITIES & EQUITY', 'indent': 0, 'bold': True, 'values': ['297,300,000', '293,750,000', '3,550,000', '282,200,000', '15,100,000']},
    ]

    cw = [3.2, 1.7, 1.7, 1.5, 1.7, 1.5]
    draw_cubeview(13, 13, 'Balance Sheet', 'CubeView: CV_Report_BS  |  Global Consolidated',
                  pov, cols, rows, col_widths=cw, show_variance_colors=True,
                  variance_col_indices={2, 4}, save_name='CV_Report_BS')


# ==============================================================================
# 8. CV_Report_CF
# ==============================================================================
def create_cf_report():
    print("Creating Cash Flow Report...")
    cols = ['Q1 2026', 'Q1 Budget', 'Variance', 'Q1 Prior', 'FY Forecast']
    pov = [('Entity', 'Global (Consolidated)'), ('Scenario', 'Actual'),
           ('Time', 'Q1 2026'), ('Method', 'Indirect')]

    rows = [
        {'label': 'Operating Activities', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Net Income', 'indent': 1, 'values': ['12,800,000', '11,500,000', '1,300,000', '10,200,000', '48,000,000']},
        {'label': 'Adjustments:', 'indent': 1, 'bold': True, 'values': ['', '', '', '', '']},
        {'label': 'Depreciation & Amortization', 'indent': 2, 'values': ['5,200,000', '5,100,000', '100,000', '4,800,000', '20,500,000']},
        {'label': 'Stock-Based Compensation', 'indent': 2, 'values': ['800,000', '750,000', '50,000', '700,000', '3,100,000']},
        {'label': 'Deferred Taxes', 'indent': 2, 'values': ['200,000', '180,000', '20,000', '150,000', '750,000']},
        {'label': 'Working Capital Changes:', 'indent': 1, 'bold': True, 'values': ['', '', '', '', '']},
        {'label': 'Change in Accounts Receivable', 'indent': 2, 'values': ['(3,600,000)', '(2,800,000)', '(800,000)', '(2,500,000)', '(5,200,000)']},
        {'label': 'Change in Inventory', 'indent': 2, 'values': ['(2,200,000)', '(1,500,000)', '(700,000)', '(1,800,000)', '(4,500,000)']},
        {'label': 'Change in Accounts Payable', 'indent': 2, 'values': ['3,600,000', '2,400,000', '1,200,000', '2,100,000', '5,800,000']},
        {'label': 'Change in Accrued Expenses', 'indent': 2, 'values': ['(400,000)', '(300,000)', '(100,000)', '(200,000)', '(800,000)']},
        {'label': 'Net Cash from Operations', 'indent': 0, 'bold': True, 'values': ['16,400,000', '15,330,000', '1,070,000', '13,450,000', '67,650,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Investing Activities', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Capital Expenditures', 'indent': 1, 'values': ['(8,500,000)', '(9,200,000)', '700,000', '(7,800,000)', '(38,000,000)']},
        {'label': 'Acquisitions', 'indent': 1, 'values': ['0', '0', '0', '(5,000,000)', '0']},
        {'label': 'Asset Disposals', 'indent': 1, 'values': ['350,000', '200,000', '150,000', '180,000', '800,000']},
        {'label': 'Net Cash from Investing', 'indent': 0, 'bold': True, 'values': ['(8,150,000)', '(9,000,000)', '850,000', '(12,620,000)', '(37,200,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Financing Activities', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Debt Repayments', 'indent': 1, 'values': ['(2,000,000)', '(2,000,000)', '0', '(1,500,000)', '(8,000,000)']},
        {'label': 'Dividends Paid', 'indent': 1, 'values': ['(3,500,000)', '(3,500,000)', '0', '(3,200,000)', '(14,000,000)']},
        {'label': 'Share Repurchases', 'indent': 1, 'values': ['(1,500,000)', '(1,000,000)', '(500,000)', '(800,000)', '(4,000,000)']},
        {'label': 'Net Cash from Financing', 'indent': 0, 'bold': True, 'values': ['(7,000,000)', '(6,500,000)', '(500,000)', '(5,500,000)', '(26,000,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'FX Effect on Cash', 'indent': 0, 'values': ['(450,000)', '(200,000)', '(250,000)', '(380,000)', '(1,200,000)']},
        {'label': 'Net Change in Cash', 'indent': 0, 'bold': True, 'values': ['800,000', '(370,000)', '1,170,000', '(5,050,000)', '3,250,000']},
        {'label': 'Beginning Cash Balance', 'indent': 0, 'values': ['27,700,000', '27,700,000', '0', '27,850,000', '27,700,000']},
        {'label': 'Ending Cash Balance', 'indent': 0, 'bold': True, 'values': ['28,500,000', '27,330,000', '1,170,000', '22,800,000', '30,950,000']},
    ]

    cw = [3.2, 1.5, 1.5, 1.3, 1.5, 1.5]
    draw_cubeview(13, 11, 'Cash Flow Statement (Indirect Method)', 'CubeView: CV_Report_CF  |  Global Consolidated',
                  pov, cols, rows, col_widths=cw, show_variance_colors=True,
                  variance_col_indices={2}, save_name='CV_Report_CF')


# ==============================================================================
# 9. CV_Report_BvA
# ==============================================================================
def create_bva_report():
    print("Creating Budget vs Actual Report...")
    cols = ['Actual', 'Budget', 'Var $', 'Var %', 'Flex Budget', 'Flex Var $']
    pov = [('Entity', 'Plant_US01_Detroit'), ('Scenario', 'Actual vs Budget'),
           ('Time', 'Q1 2026'), ('Product', 'All Products')]

    rows = [
        {'label': 'Net Revenue', 'indent': 0, 'bold': True, 'values': ['20,674,000', '20,100,000', '574,000', '2.9%', '20,450,000', '224,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Cost of Goods Sold', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'Direct Materials', 'indent': 1, 'values': ['(6,850,000)', '(6,700,000)', '(150,000)', '2.2%', '(6,820,000)', '(30,000)']},
        {'label': 'Direct Labor', 'indent': 1, 'values': ['(3,100,000)', '(3,020,000)', '(80,000)', '2.6%', '(3,075,000)', '(25,000)']},
        {'label': 'Manufacturing Overhead', 'indent': 1, 'values': ['(2,200,000)', '(2,250,000)', '50,000', '-2.2%', '(2,230,000)', '30,000']},
        {'label': 'Price Variance', 'indent': 2, 'values': ['(85,000)', '0', '(85,000)', 'N/A', '0', '(85,000)']},
        {'label': 'Usage Variance', 'indent': 2, 'values': ['(42,000)', '0', '(42,000)', 'N/A', '0', '(42,000)']},
        {'label': 'Efficiency Variance', 'indent': 2, 'values': ['(28,000)', '0', '(28,000)', 'N/A', '0', '(28,000)']},
        {'label': 'Volume Variance', 'indent': 2, 'values': ['35,000', '0', '35,000', 'N/A', '0', '35,000']},
        {'label': 'Total COGS', 'indent': 0, 'bold': True, 'values': ['(12,270,000)', '(11,970,000)', '(300,000)', '2.5%', '(12,125,000)', '(145,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Gross Profit', 'indent': 0, 'bold': True, 'values': ['8,404,000', '8,130,000', '274,000', '3.4%', '8,325,000', '79,000']},
        {'label': 'Gross Margin %', 'indent': 1, 'values': ['40.6%', '40.4%', '0.2%', '', '40.7%', '-0.1%']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Operating Expenses', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '', '']},
        {'label': 'SG&A', 'indent': 1, 'values': ['(1,479,500)', '(1,520,000)', '40,500', '-2.7%', '(1,510,000)', '30,500']},
        {'label': 'R&D', 'indent': 1, 'values': ['(738,000)', '(750,000)', '12,000', '-1.6%', '(748,000)', '10,000']},
        {'label': 'Marketing', 'indent': 1, 'values': ['(245,000)', '(260,000)', '15,000', '-5.8%', '(258,000)', '13,000']},
        {'label': 'Total OPEX', 'indent': 0, 'bold': True, 'values': ['(2,462,500)', '(2,530,000)', '67,500', '-2.7%', '(2,516,000)', '53,500']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'EBITDA', 'indent': 0, 'bold': True, 'values': ['5,941,500', '5,600,000', '341,500', '6.1%', '5,809,000', '132,500']},
        {'label': 'EBITDA Margin %', 'indent': 1, 'values': ['28.7%', '27.9%', '0.9%', '', '28.4%', '0.3%']},
    ]

    cw = [3.0, 1.4, 1.4, 1.3, 0.9, 1.4, 1.3]
    draw_cubeview(13, 9, 'Budget vs Actual Variance Analysis', 'CubeView: CV_Report_BvA  |  With Flex Budget',
                  pov, cols, rows, col_widths=cw, show_variance_colors=True,
                  variance_col_indices={2, 3, 5}, save_name='CV_Report_BvA')


# ==============================================================================
# 10. CV_Report_Consolidation
# ==============================================================================
def create_consolidation_report():
    print("Creating Consolidation Report...")
    cols = ['Local', 'FX Translation', 'IC Elimination', 'Minority Int', 'Consolidated']
    pov = [('Entity', 'Global'), ('Scenario', 'Actual'),
           ('Time', 'Q1 2026'), ('Flow', 'Closing')]

    rows = [
        {'label': 'Revenue', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Third Party Revenue', 'indent': 1, 'values': ['152,400,000', '(3,200,000)', '0', '0', '149,200,000']},
        {'label': 'Intercompany Revenue', 'indent': 1, 'values': ['28,500,000', '(600,000)', '(27,900,000)', '0', '0']},
        {'label': 'Net Revenue', 'indent': 0, 'bold': True, 'values': ['180,900,000', '(3,800,000)', '(27,900,000)', '0', '149,200,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Cost of Goods Sold', 'indent': 0, 'bold': True, 'section': True, 'values': ['', '', '', '', '']},
        {'label': 'Third Party COGS', 'indent': 1, 'values': ['(98,500,000)', '2,100,000', '0', '0', '(96,400,000)']},
        {'label': 'Intercompany COGS', 'indent': 1, 'values': ['(25,800,000)', '500,000', '25,300,000', '0', '0']},
        {'label': 'IC Profit Elimination', 'indent': 1, 'values': ['0', '0', '(2,600,000)', '0', '(2,600,000)']},
        {'label': 'Total COGS', 'indent': 0, 'bold': True, 'values': ['(124,300,000)', '2,600,000', '22,700,000', '0', '(99,000,000)']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Gross Profit', 'indent': 0, 'bold': True, 'values': ['56,600,000', '(1,200,000)', '(5,200,000)', '0', '50,200,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Operating Expenses', 'indent': 0, 'values': ['(28,500,000)', '580,000', '0', '0', '(27,920,000)']},
        {'label': 'EBIT', 'indent': 0, 'bold': True, 'values': ['28,100,000', '(620,000)', '(5,200,000)', '0', '22,280,000']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Interest & Other', 'indent': 0, 'values': ['(2,800,000)', '60,000', '350,000', '0', '(2,390,000)']},
        {'label': 'IC Interest Elimination', 'indent': 1, 'values': ['0', '0', '350,000', '0', '350,000']},
        {'label': 'EBT', 'indent': 0, 'bold': True, 'values': ['25,300,000', '(560,000)', '(4,850,000)', '0', '19,890,000']},
        {'label': 'Income Tax', 'indent': 0, 'values': ['(6,325,000)', '140,000', '1,212,500', '0', '(4,972,500)']},
        {'label': 'Net Income', 'indent': 0, 'bold': True, 'values': ['18,975,000', '(420,000)', '(3,637,500)', '0', '14,917,500']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'Less: Minority Interest', 'indent': 0, 'values': ['0', '0', '0', '(1,200,000)', '(1,200,000)']},
        {'label': 'Net Income Attr to Parent', 'indent': 0, 'bold': True, 'values': ['18,975,000', '(420,000)', '(3,637,500)', '(1,200,000)', '13,717,500']},
        {'label': '', 'indent': 0, 'separator': True, 'values': []},
        {'label': 'CTA (OCI)', 'indent': 0, 'bold': True, 'values': ['0', '(2,350,000)', '0', '0', '(2,350,000)']},
    ]

    cw = [3.0, 1.6, 1.6, 1.6, 1.5, 1.6]
    draw_cubeview(13, 10.5, 'Consolidation Report', 'CubeView: CV_Report_Consolidation  |  Full Elimination Detail',
                  pov, cols, rows, col_widths=cw, save_name='CV_Report_Consolidation')


# ==============================================================================
# RUN ALL
# ==============================================================================
if __name__ == '__main__':
    print("Generating OneStream CubeView Mockups...\n")
    create_revenue_entry()
    create_opex_entry()
    create_headcount_entry()
    create_capex_entry()
    create_production_entry()
    create_pl_report()
    create_bs_report()
    create_cf_report()
    create_bva_report()
    create_consolidation_report()
    print(f"\nAll mockups saved to: {OUTPUT_DIR}")
    print("Done!")
