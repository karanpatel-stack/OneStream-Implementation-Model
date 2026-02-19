"""
generate_mockups.py
Generates 16 realistic OneStream dashboard mockup PNGs using matplotlib.
Each dashboard is a widescreen (16x9 inches, 150 DPI) image with a dark navy
title bar, POV/filter bar, and a grid layout of visual components.
"""

import os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.gridspec as gridspec
import matplotlib.patches as mpatches
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch, Wedge
from matplotlib.collections import PatchCollection
import matplotlib.patheffects as pe
from matplotlib import colormaps

# ── Colour palette ──────────────────────────────────────────────────────────
NAVY       = "#0B1D3A"
ACCENT_BLUE = "#006EC7"
GREEN      = "#28A745"
RED        = "#DC3545"
ORANGE     = "#FD7E14"
GOLD       = "#FFC107"
LIGHT_GRAY = "#F0F4F8"
DARK_GRAY  = "#343A40"
WHITE      = "#FFFFFF"
MID_GRAY   = "#A0AEC0"
BLUE_LIGHT = "#4DA3E8"
TEAL       = "#17A2B8"

OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Mockups")
os.makedirs(OUTPUT_DIR, exist_ok=True)

DPI = 150
FIG_W, FIG_H = 16, 9


# ── Utility helpers ─────────────────────────────────────────────────────────

def _new_fig(title: str, pov_text: str = "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD"):
    """Create figure with standard title bar and POV bar. Returns (fig, gs_main)."""
    fig = plt.figure(figsize=(FIG_W, FIG_H), facecolor=LIGHT_GRAY)

    # Title bar (top 6%)
    ax_title = fig.add_axes([0, 0.94, 1, 0.06], facecolor=NAVY)
    ax_title.set_xlim(0, 1); ax_title.set_ylim(0, 1)
    ax_title.axis("off")
    ax_title.text(0.01, 0.5, "OneStream", color=ACCENT_BLUE, fontsize=14,
                  fontweight="bold", va="center", fontfamily="sans-serif")
    ax_title.text(0.08, 0.5, title, color=WHITE, fontsize=16,
                  fontweight="bold", va="center", fontfamily="sans-serif")
    ax_title.text(0.99, 0.5, "Admin  |  Logout", color=MID_GRAY, fontsize=9,
                  va="center", ha="right", fontfamily="sans-serif")

    # POV bar (next 3.5%)
    ax_pov = fig.add_axes([0, 0.905, 1, 0.035], facecolor="#E8EEF4")
    ax_pov.set_xlim(0, 1); ax_pov.set_ylim(0, 1)
    ax_pov.axis("off")
    ax_pov.text(0.01, 0.5, pov_text, color=DARK_GRAY, fontsize=9,
                va="center", fontfamily="sans-serif")

    return fig


def _kpi_tile(ax, label, value, delta=None, delta_color=GREEN, sparkdata=None):
    """Draw a KPI tile on the given axes."""
    ax.set_facecolor(WHITE)
    for spine in ax.spines.values():
        spine.set_edgecolor("#D0D8E0")
        spine.set_linewidth(0.8)
    ax.set_xticks([]); ax.set_yticks([])
    ax.set_xlim(0, 1); ax.set_ylim(0, 1)

    ax.text(0.5, 0.82, label, color=MID_GRAY, fontsize=8, ha="center", va="center",
            fontfamily="sans-serif", fontweight="bold")
    ax.text(0.5, 0.48, str(value), color=NAVY, fontsize=18, ha="center", va="center",
            fontfamily="sans-serif", fontweight="bold")
    if delta is not None:
        arrow = "\u25B2" if delta_color == GREEN else "\u25BC"
        ax.text(0.5, 0.18, f"{arrow} {delta}", color=delta_color, fontsize=10,
                ha="center", va="center", fontfamily="sans-serif", fontweight="bold")
    if sparkdata is not None:
        ins = ax.inset_axes([0.15, 0.02, 0.7, 0.25])
        ins.plot(sparkdata, color=ACCENT_BLUE, linewidth=1.2)
        ins.fill_between(range(len(sparkdata)), sparkdata, alpha=0.1, color=ACCENT_BLUE)
        ins.axis("off")


def _save(fig, fname):
    path = os.path.join(OUTPUT_DIR, fname)
    fig.savefig(path, dpi=DPI, bbox_inches="tight", facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Saved {fname}")
    return path


# ── 1. DB_ExecutiveSummary ──────────────────────────────────────────────────

def db_executive_summary():
    fig = _new_fig("Executive Summary Dashboard")

    # KPI row
    kpi_data = [
        ("Revenue", "$149.2M", "+9.4%", GREEN),
        ("Gross Margin", "33.7%", "+1.2pp", GREEN),
        ("EBITDA", "$22.3M", "+16.9%", GREEN),
        ("Net Income", "$14.9M", "+8.5%", GREEN),
        ("ROIC", "12.8%", "+0.6pp", GREEN),
        ("Free Cash Flow", "$8.3M", "-2.1%", RED),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpi_data):
        ax = fig.add_axes([0.015 + i * 0.163, 0.76, 0.155, 0.13], facecolor=WHITE)
        spark = np.cumsum(np.random.randn(12)) + 50
        _kpi_tile(ax, lbl, val, delta, dc, spark)

    # Revenue trend
    ax_rev = fig.add_axes([0.04, 0.39, 0.44, 0.34])
    months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    rev = [10.2, 10.8, 11.3, 11.0, 12.1, 12.8, 13.2, 12.5, 13.0, 13.5, 14.0, 14.8]
    ax_rev.plot(months, rev, color=ACCENT_BLUE, linewidth=2.5, marker="o", markersize=5)
    ax_rev.fill_between(months, rev, alpha=0.08, color=ACCENT_BLUE)
    ax_rev.set_title("Revenue Trend (FY2025, $M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_rev.set_facecolor(WHITE)
    ax_rev.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_rev.set_ylim(8, 16)
    ax_rev.tick_params(labelsize=8, colors=DARK_GRAY)

    # Margin trend
    ax_mg = fig.add_axes([0.54, 0.39, 0.44, 0.34])
    gm = [31.2, 32.0, 32.5, 33.0, 33.1, 33.4, 33.8, 34.0, 33.5, 33.9, 34.2, 33.7]
    nm = [8.0, 8.5, 9.0, 9.2, 9.5, 10.0, 10.2, 9.8, 10.1, 10.5, 10.8, 10.0]
    ax_mg.plot(months, gm, color=GREEN, linewidth=2.5, marker="o", markersize=5, label="Gross Margin %")
    ax_mg.plot(months, nm, color=ACCENT_BLUE, linewidth=2.5, marker="s", markersize=4, label="Net Margin %")
    ax_mg.set_title("Margin Trends (FY2025)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_mg.legend(fontsize=8, loc="lower right")
    ax_mg.set_facecolor(WHITE)
    ax_mg.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_mg.tick_params(labelsize=8, colors=DARK_GRAY)

    # Entity table
    ax_tbl = fig.add_axes([0.04, 0.03, 0.92, 0.32])
    ax_tbl.axis("off")
    ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Entity Performance – Top 10", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Entity", "Revenue ($M)", "Gross Margin %", "EBITDA ($M)", "Net Income ($M)", "Variance %", "Status"]
    rows = [
        ["US01 - North America", "42.8", "35.2%", "7.8", "5.1", "+12.3%", "G"],
        ["US02 - South Region", "28.5", "33.1%", "4.9", "3.2", "+8.7%", "G"],
        ["DE01 - Germany", "22.1", "31.8%", "3.5", "2.3", "+5.2%", "G"],
        ["UK01 - United Kingdom", "15.9", "34.5%", "2.8", "1.8", "+3.1%", "Y"],
        ["CN01 - China", "14.2", "29.4%", "2.1", "1.2", "-2.4%", "R"],
        ["JP01 - Japan", "10.3", "36.1%", "2.0", "1.4", "+6.8%", "G"],
        ["FR01 - France", "5.8", "30.2%", "0.8", "0.4", "-1.1%", "Y"],
        ["AU01 - Australia", "4.1", "32.7%", "0.6", "0.3", "+4.2%", "G"],
        ["BR01 - Brazil", "3.3", "27.5%", "0.4", "0.1", "-5.6%", "R"],
        ["IN01 - India", "2.2", "28.9%", "0.3", "0.1", "+2.0%", "Y"],
    ]
    cell_colors = []
    for r in rows:
        row_c = [LIGHT_GRAY] * 6
        sc = r[-1]
        row_c.append(GREEN if sc == "G" else (GOLD if sc == "Y" else RED))
        cell_colors.append(row_c)
    display_rows = [r[:-1] + [{"G": "On Track", "Y": "Monitor", "R": "At Risk"}[r[-1]]] for r in rows]
    tbl = ax_tbl.table(cellText=display_rows, colLabels=cols, loc="center",
                       cellLoc="center", cellColours=cell_colors,
                       colColours=[NAVY]*7)
    tbl.auto_set_font_size(False)
    tbl.set_fontsize(7.5)
    tbl.scale(1, 1.35)
    for (row, col), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if row == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
            cell.set_facecolor(NAVY)
        elif col == 6:
            cell.set_text_props(color=WHITE, fontweight="bold")

    _save(fig, "DB_ExecutiveSummary.png")


# ── 2. DB_ConsolidationStatus ──────────────────────────────────────────────

def db_consolidation_status():
    fig = _new_fig("Consolidation Status Tracker",
                   "Scenario: Actual  |  Time: FY2025.Dec  |  Close Period: Month-End")

    # Progress bar
    ax_prog = fig.add_axes([0.04, 0.82, 0.92, 0.07], facecolor=WHITE)
    ax_prog.set_xlim(0, 100); ax_prog.set_ylim(0, 1)
    ax_prog.barh(0.5, 62, height=0.6, color=ACCENT_BLUE, edgecolor="none")
    ax_prog.barh(0.5, 100, height=0.6, color="#E0E0E0", edgecolor="none", zorder=0)
    ax_prog.barh(0.5, 62, height=0.6, color=ACCENT_BLUE, edgecolor="none", zorder=1)
    ax_prog.text(50, 0.5, "Month-End Close: Day 5 of 8  \u2014  62% Complete",
                 ha="center", va="center", color=WHITE, fontsize=11, fontweight="bold", zorder=2)
    ax_prog.axis("off")

    # Status grid
    entities = ["US01", "US02", "DE01", "UK01", "CN01", "JP01", "FR01", "AU01", "BR01", "IN01", "MX01", "SG01"]
    steps = ["Data Load", "Local Adj", "IC Recon", "FX Transl.", "Consolidation", "Review", "Certified"]
    # 2=complete(green), 1=in-progress(gold), 0=not started(gray)
    status_matrix = [
        [2,2,2,2,2,2,2],
        [2,2,2,2,2,1,0],
        [2,2,2,2,1,0,0],
        [2,2,2,2,2,1,0],
        [2,2,2,1,0,0,0],
        [2,2,2,2,2,2,1],
        [2,2,1,0,0,0,0],
        [2,2,2,2,1,0,0],
        [2,1,0,0,0,0,0],
        [2,2,2,1,0,0,0],
        [2,2,2,2,2,1,0],
        [2,2,2,2,2,2,2],
    ]
    color_map = {2: GREEN, 1: GOLD, 0: "#C0C8D0"}

    ax_grid = fig.add_axes([0.04, 0.04, 0.92, 0.75], facecolor=WHITE)
    ax_grid.set_xlim(-0.5, len(steps) - 0.5)
    ax_grid.set_ylim(-0.5, len(entities) - 0.5)
    ax_grid.set_xticks(range(len(steps)))
    ax_grid.set_xticklabels(steps, fontsize=9, fontweight="bold", color=NAVY)
    ax_grid.set_yticks(range(len(entities)))
    ax_grid.set_yticklabels(entities[::-1], fontsize=10, fontweight="bold", color=NAVY)
    ax_grid.tick_params(length=0)
    ax_grid.set_facecolor(WHITE)
    for spine in ax_grid.spines.values():
        spine.set_visible(False)

    for i, ent in enumerate(entities):
        for j, step in enumerate(steps):
            s = status_matrix[i][j]
            c = color_map[s]
            circle = plt.Circle((j, len(entities) - 1 - i), 0.3, color=c, zorder=3)
            ax_grid.add_patch(circle)
            if s == 2:
                ax_grid.text(j, len(entities)-1-i, "\u2713", ha="center", va="center",
                             color=WHITE, fontsize=12, fontweight="bold", zorder=4)
            elif s == 1:
                ax_grid.text(j, len(entities)-1-i, "\u2022\u2022\u2022", ha="center", va="center",
                             color=WHITE, fontsize=8, fontweight="bold", zorder=4)

    # Legend
    ax_grid.text(0, -0.45, "\u25CF Complete", color=GREEN, fontsize=9, fontweight="bold", va="top")
    ax_grid.text(1.6, -0.45, "\u25CF In Progress", color=GOLD, fontsize=9, fontweight="bold", va="top")
    ax_grid.text(3.4, -0.45, "\u25CF Not Started", color="#C0C8D0", fontsize=9, fontweight="bold", va="top")

    # Horizontal grid lines
    for i in range(len(entities)):
        ax_grid.axhline(i + 0.5, color="#E8EEF4", linewidth=0.5)

    _save(fig, "DB_ConsolidationStatus.png")


# ── 3. DB_PlantPerformance ─────────────────────────────────────────────────

def db_plant_performance():
    fig = _new_fig("Plant Performance Dashboard",
                   "Entity: MFG Plants  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD")

    # KPI tiles
    kpis = [
        ("OEE", "82.1%", "+2.3pp", GREEN),
        ("Capacity Utilization", "84.4%", "+1.1pp", GREEN),
        ("First Pass Yield", "96.5%", "+0.4pp", GREEN),
        ("Scrap Rate", "2.1%", "-0.3pp", GREEN),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpis):
        ax = fig.add_axes([0.02 + i * 0.245, 0.78, 0.23, 0.1], facecolor=WHITE)
        _kpi_tile(ax, lbl, val, delta, dc)

    # OEE by plant bar chart
    ax_bar = fig.add_axes([0.04, 0.38, 0.44, 0.36])
    plants = ["US01", "US02", "DE01", "UK01", "CN01", "JP01"]
    oee = [82, 79, 85, 81, 77, 88]
    colors_bar = [ACCENT_BLUE if v >= 80 else ORANGE for v in oee]
    bars = ax_bar.bar(plants, oee, color=colors_bar, width=0.55, edgecolor="none")
    ax_bar.set_title("OEE by Plant (%)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_bar.set_ylim(60, 100)
    ax_bar.set_facecolor(WHITE)
    ax_bar.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_bar.tick_params(labelsize=9, colors=DARK_GRAY)
    ax_bar.axhline(80, color=RED, linewidth=1, linestyle="--", label="Target 80%")
    ax_bar.legend(fontsize=8)
    for bar, v in zip(bars, oee):
        ax_bar.text(bar.get_x() + bar.get_width()/2, bar.get_height() + 0.8,
                    f"{v}%", ha="center", va="bottom", fontsize=9, fontweight="bold", color=NAVY)

    # Plant metrics table
    ax_tbl = fig.add_axes([0.54, 0.38, 0.44, 0.36])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Plant Metrics Summary", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Plant", "Prod Vol (K)", "Mach Hrs (K)", "Labor Hrs (K)", "Cost/Unit ($)"]
    data = [
        ["US01", "124.5", "18.2", "42.8", "41.20"],
        ["US02", "98.3", "14.8", "35.1", "43.50"],
        ["DE01", "112.7", "16.5", "38.9", "45.80"],
        ["UK01", "87.1", "12.9", "30.2", "44.10"],
        ["CN01", "145.2", "21.0", "52.3", "38.90"],
        ["JP01", "76.8", "11.2", "26.4", "46.30"],
    ]
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center",
                       colColours=[NAVY]*5)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8.5); tbl.scale(1, 1.45)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
        else:
            cell.set_facecolor(WHITE)

    # Bottom trend
    ax_trend = fig.add_axes([0.04, 0.04, 0.92, 0.30])
    months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    for plant, vals, clr in [("US01", [80,81,79,82,83,81,82,84,83,82,81,82], ACCENT_BLUE),
                              ("DE01", [83,84,82,85,84,86,85,86,85,84,85,85], GREEN),
                              ("JP01", [85,86,87,86,88,87,88,89,88,87,88,88], TEAL)]:
        ax_trend.plot(months, vals, linewidth=2, marker="o", markersize=4, label=plant, color=clr)
    ax_trend.set_title("OEE Trend by Key Plant (%)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_trend.legend(fontsize=8); ax_trend.set_facecolor(WHITE)
    ax_trend.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_trend.set_ylim(70, 95)
    ax_trend.tick_params(labelsize=8, colors=DARK_GRAY)

    _save(fig, "DB_PlantPerformance.png")


# ── 4. DB_ProductionVariance ───────────────────────────────────────────────

def _waterfall(ax, categories, values, start_val, title, fmt="${}M"):
    """Generic waterfall chart helper."""
    n = len(categories)
    running = start_val
    bottoms = []
    bar_vals = []
    colors = []
    for i, v in enumerate(values):
        if i == 0:
            bottoms.append(0)
            bar_vals.append(v)
            colors.append(ACCENT_BLUE)
        elif i == n - 1:
            bottoms.append(0)
            bar_vals.append(running + v if v != 0 else running)
            colors.append(ACCENT_BLUE)
        else:
            if v >= 0:
                bottoms.append(running)
                bar_vals.append(v)
                colors.append(GREEN)
            else:
                bottoms.append(running + v)
                bar_vals.append(abs(v))
                colors.append(RED)
            running += v

    bars = ax.bar(categories, bar_vals, bottom=bottoms, color=colors, width=0.55, edgecolor="none")
    for i, (bar, v) in enumerate(zip(bars, values)):
        y = bottoms[i] + bar_vals[i] + 0.05 * start_val
        lbl = fmt.format(f"{v:+.1f}") if (i > 0 and i < n-1) else fmt.format(f"{bar_vals[i]:.1f}")
        ax.text(bar.get_x() + bar.get_width()/2, y, lbl,
                ha="center", va="bottom", fontsize=8, fontweight="bold", color=NAVY)

    # Connector lines
    for i in range(n - 1):
        top = bottoms[i] + bar_vals[i]
        ax.plot([i + 0.275, i + 0.725], [top, top], color=MID_GRAY, linewidth=0.8, linestyle="--")

    ax.set_title(title, fontsize=12, fontweight="bold", color=NAVY, loc="left")
    ax.set_facecolor(WHITE)
    ax.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax.tick_params(labelsize=8, colors=DARK_GRAY)


def db_production_variance():
    fig = _new_fig("Production Variance Analysis",
                   "Entity: MFG Plants  |  Scenario: Actual vs Budget  |  Time: FY2025.Dec")

    ax_wf = fig.add_axes([0.04, 0.35, 0.92, 0.52])
    cats = ["Budget", "Volume", "Price", "Mix", "FX", "Input\nCost", "Labor", "Overhead", "Actual"]
    vals = [8.1, 0.6, 0.3, -0.1, -0.2, -0.4, 0.1, 0.1, 0]  # last is placeholder
    # compute running to get actual
    running = 8.1
    for v in vals[1:-1]:
        running += v
    vals[-1] = 0  # we handle actual separately
    n = len(cats)
    run = vals[0]
    bottoms = []; bar_vals = []; colors = []
    for i, v in enumerate(vals):
        if i == 0:
            bottoms.append(0); bar_vals.append(v); colors.append(ACCENT_BLUE)
        elif i == n-1:
            bottoms.append(0); bar_vals.append(running); colors.append(ACCENT_BLUE)
        else:
            if v >= 0:
                bottoms.append(run); bar_vals.append(v); colors.append(GREEN)
            else:
                bottoms.append(run + v); bar_vals.append(abs(v)); colors.append(RED)
            run += v

    bars = ax_wf.bar(cats, bar_vals, bottom=bottoms, color=colors, width=0.55, edgecolor="none")
    for i, (bar, v) in enumerate(zip(bars, vals)):
        y = bottoms[i] + bar_vals[i] + 0.05
        if i == 0:
            lbl = f"${bar_vals[i]:.1f}M"
        elif i == n-1:
            lbl = f"${running:.1f}M"
        else:
            lbl = f"{'+'if v>=0 else ''}{v:.1f}M"
        ax_wf.text(bar.get_x()+bar.get_width()/2, y, lbl,
                   ha="center", va="bottom", fontsize=9, fontweight="bold", color=NAVY)
    for i in range(n-1):
        top = bottoms[i] + bar_vals[i]
        ax_wf.plot([i+0.275, i+0.725], [top, top], color=MID_GRAY, linewidth=0.8, linestyle="--")

    ax_wf.set_title("Production Cost Variance Waterfall ($M)", fontsize=12, fontweight="bold", color=NAVY, loc="left")
    ax_wf.set_facecolor(WHITE)
    ax_wf.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_wf.tick_params(labelsize=8, colors=DARK_GRAY)
    ax_wf.set_ylim(0, 10)

    # Summary table
    ax_tbl = fig.add_axes([0.04, 0.03, 0.92, 0.28])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Variance Detail by Plant", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Plant", "Budget ($M)", "Actual ($M)", "Variance ($M)", "Var %", "Key Driver"]
    data = [
        ["US01", "2.40", "2.35", "-0.05", "-2.1%", "Favorable labor"],
        ["US02", "1.85", "1.92", "+0.07", "+3.8%", "Input cost increase"],
        ["DE01", "1.50", "1.48", "-0.02", "-1.3%", "Yield improvement"],
        ["UK01", "0.95", "0.98", "+0.03", "+3.2%", "FX headwind"],
        ["CN01", "0.90", "1.28", "+0.38", "+42.2%", "Volume ramp-up"],
        ["JP01", "0.50", "0.49", "-0.01", "-2.0%", "Efficiency gain"],
    ]
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center", colColours=[NAVY]*6)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8.5); tbl.scale(1, 1.4)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
        else:
            cell.set_facecolor(WHITE)

    _save(fig, "DB_ProductionVariance.png")


# ── 5. DB_PLWaterfall ──────────────────────────────────────────────────────

def db_pl_waterfall():
    fig = _new_fig("P&L Bridge Analysis",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025 vs FY2024  |  View: Full Year")

    ax = fig.add_axes([0.04, 0.15, 0.92, 0.7])
    cats = ["Prior Year\nNet Income", "Revenue\nGrowth", "COGS\nChange", "OPEX\nChange",
            "D&A", "Interest", "Tax\nImpact", "Current Year\nNet Income"]
    vals = [3.84, 1.52, -0.72, -0.35, 0.12, -0.08, 0.40, 0]
    running = 3.84
    for v in vals[1:-1]:
        running += v
    n = len(cats)
    run = vals[0]; bottoms = []; bar_vals = []; colors = []
    for i, v in enumerate(vals):
        if i == 0:
            bottoms.append(0); bar_vals.append(v); colors.append(ACCENT_BLUE)
        elif i == n-1:
            bottoms.append(0); bar_vals.append(running); colors.append(ACCENT_BLUE)
        else:
            if v >= 0:
                bottoms.append(run); bar_vals.append(v); colors.append(GREEN)
            else:
                bottoms.append(run + v); bar_vals.append(abs(v)); colors.append(RED)
            run += v

    bars = ax.bar(cats, bar_vals, bottom=bottoms, color=colors, width=0.55, edgecolor="none")
    for i, (bar, v) in enumerate(zip(bars, vals)):
        y = bottoms[i] + bar_vals[i] + 0.05
        if i == 0:
            lbl = f"${bar_vals[i]:.2f}M"
        elif i == n-1:
            lbl = f"${running:.2f}M"
        else:
            lbl = f"{'+'if v>=0 else ''}{v:.2f}M"
        ax.text(bar.get_x()+bar.get_width()/2, y, lbl,
                ha="center", va="bottom", fontsize=9, fontweight="bold", color=NAVY)
    for i in range(n-1):
        top = bottoms[i] + bar_vals[i]
        ax.plot([i+0.275, i+0.725], [top, top], color=MID_GRAY, linewidth=0.8, linestyle="--")

    ax.set_title("Net Income Bridge: FY2024 to FY2025 ($M)", fontsize=13, fontweight="bold", color=NAVY, loc="left")
    ax.set_facecolor(WHITE)
    ax.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax.tick_params(labelsize=8.5, colors=DARK_GRAY)
    ax.set_ylim(0, 6.5)

    # Annotation
    ax.annotate("", xy=(7, running + 0.15), xytext=(0, vals[0] + 0.15),
                arrowprops=dict(arrowstyle="->", color=GREEN, lw=1.5))
    ax.text(3.5, max(running, vals[0]) + 0.35, f"+${running - vals[0]:.2f}M  (+{(running/vals[0]-1)*100:.0f}%)",
            ha="center", va="bottom", fontsize=10, fontweight="bold", color=GREEN)

    _save(fig, "DB_PLWaterfall.png")


# ── 6. DB_BalanceSheet ─────────────────────────────────────────────────────

def db_balance_sheet():
    fig = _new_fig("Balance Sheet Overview",
                   "Entity: CONSOL  |  Scenario: Actual  |  Time: FY2025.Dec")

    # Asset stacked bar
    ax_a = fig.add_axes([0.05, 0.38, 0.4, 0.48])
    periods = ["FY2023", "FY2024", "FY2025"]
    current_assets = [45, 52, 59]
    ppe = [38, 42, 48]
    intangibles = [12, 14, 16]
    goodwill = [22, 22, 22]
    ax_a.bar(periods, current_assets, color=ACCENT_BLUE, label="Current Assets", width=0.45)
    ax_a.bar(periods, ppe, bottom=current_assets, color=TEAL, label="Net PP&E", width=0.45)
    bot2 = [a+b for a,b in zip(current_assets, ppe)]
    ax_a.bar(periods, intangibles, bottom=bot2, color=GOLD, label="Intangibles", width=0.45)
    bot3 = [a+b for a,b in zip(bot2, intangibles)]
    ax_a.bar(periods, goodwill, bottom=bot3, color=MID_GRAY, label="Goodwill", width=0.45)
    ax_a.set_title("Total Assets ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_a.legend(fontsize=7.5, loc="upper left")
    ax_a.set_facecolor(WHITE)
    ax_a.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_a.tick_params(labelsize=9, colors=DARK_GRAY)

    # L+E stacked bar
    ax_l = fig.add_axes([0.55, 0.38, 0.4, 0.48])
    cl = [35, 38, 42]
    ltd = [30, 32, 33]
    equity = [52, 60, 70]
    ax_l.bar(periods, cl, color=RED, label="Current Liabilities", width=0.45, alpha=0.85)
    ax_l.bar(periods, ltd, bottom=cl, color=ORANGE, label="Long-Term Debt", width=0.45, alpha=0.85)
    bot_e = [a+b for a,b in zip(cl, ltd)]
    ax_l.bar(periods, equity, bottom=bot_e, color=GREEN, label="Equity", width=0.45, alpha=0.85)
    ax_l.set_title("Liabilities & Equity ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_l.legend(fontsize=7.5, loc="upper left")
    ax_l.set_facecolor(WHITE)
    ax_l.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_l.tick_params(labelsize=9, colors=DARK_GRAY)

    # Key ratios
    ax_r = fig.add_axes([0.05, 0.04, 0.9, 0.28])
    ax_r.axis("off"); ax_r.set_facecolor(WHITE)
    ax_r.set_title("Key Balance Sheet Ratios", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Ratio", "FY2023", "FY2024", "FY2025", "Trend"]
    data = [
        ["Current Ratio", "1.29", "1.37", "2.03", "\u25B2"],
        ["Quick Ratio", "0.88", "1.02", "1.23", "\u25B2"],
        ["Debt-to-Equity", "1.25", "1.17", "0.93", "\u25BC (improved)"],
        ["Book Value / Share", "$26.00", "$30.00", "$35.00", "\u25B2"],
        ["Total Assets ($M)", "$117.0", "$130.0", "$145.0", "\u25B2"],
        ["Working Capital ($M)", "$10.0", "$14.0", "$17.0", "\u25B2"],
    ]
    tbl = ax_r.table(cellText=data, colLabels=cols, loc="center", cellLoc="center", colColours=[NAVY]*5)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8.5); tbl.scale(1, 1.5)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
        else:
            cell.set_facecolor(WHITE)

    _save(fig, "DB_BalanceSheet.png")


# ── 7. DB_CashFlow ────────────────────────────────────────────────────────

def db_cash_flow():
    fig = _new_fig("Cash Flow Dashboard",
                   "Entity: CONSOL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: Full Year")

    # Waterfall
    ax_wf = fig.add_axes([0.04, 0.45, 0.92, 0.4])
    cats = ["Beginning\nCash", "Operating\nCF", "Investing\nCF", "Financing\nCF", "FX\nEffect", "Ending\nCash"]
    vals = [27.7, 18.2, -12.5, -4.1, -0.8, 0]
    running = 27.7
    for v in vals[1:-1]:
        running += v
    n = len(cats)
    run = vals[0]; bottoms = []; bar_vals = []; colors = []
    for i, v in enumerate(vals):
        if i == 0:
            bottoms.append(0); bar_vals.append(v); colors.append(ACCENT_BLUE)
        elif i == n-1:
            bottoms.append(0); bar_vals.append(running); colors.append(ACCENT_BLUE)
        else:
            if v >= 0:
                bottoms.append(run); bar_vals.append(v); colors.append(GREEN)
            else:
                bottoms.append(run + v); bar_vals.append(abs(v)); colors.append(RED)
            run += v

    bars = ax_wf.bar(cats, bar_vals, bottom=bottoms, color=colors, width=0.5, edgecolor="none")
    for i, (bar, v) in enumerate(zip(bars, vals)):
        y = bottoms[i] + bar_vals[i] + 0.3
        if i == 0: lbl = f"${vals[0]:.1f}M"
        elif i == n-1: lbl = f"${running:.1f}M"
        else: lbl = f"{'+'if v>=0 else ''}{v:.1f}M"
        ax_wf.text(bar.get_x()+bar.get_width()/2, y, lbl,
                   ha="center", va="bottom", fontsize=9.5, fontweight="bold", color=NAVY)
    for i in range(n-1):
        top = bottoms[i] + bar_vals[i]
        ax_wf.plot([i+0.25, i+0.75], [top, top], color=MID_GRAY, linewidth=0.8, linestyle="--")
    ax_wf.set_title("Cash Flow Waterfall ($M)", fontsize=12, fontweight="bold", color=NAVY, loc="left")
    ax_wf.set_facecolor(WHITE)
    ax_wf.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_wf.tick_params(labelsize=9, colors=DARK_GRAY)
    ax_wf.set_ylim(0, 52)

    # Monthly cash balance trend
    ax_tr = fig.add_axes([0.04, 0.05, 0.92, 0.35])
    months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    cash = [27.7, 26.5, 25.8, 27.2, 28.1, 27.5, 26.9, 28.3, 29.0, 28.2, 27.8, 28.5]
    ax_tr.plot(months, cash, color=ACCENT_BLUE, linewidth=2.5, marker="o", markersize=5)
    ax_tr.fill_between(months, cash, alpha=0.08, color=ACCENT_BLUE)
    ax_tr.axhline(25.0, color=RED, linewidth=1, linestyle="--", label="Min Threshold $25M")
    ax_tr.set_title("Monthly Cash Balance ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_tr.legend(fontsize=8)
    ax_tr.set_facecolor(WHITE)
    ax_tr.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_tr.tick_params(labelsize=8, colors=DARK_GRAY)
    ax_tr.set_ylim(22, 32)

    _save(fig, "DB_CashFlow.png")


# ── 8. DB_BudgetVsActual ──────────────────────────────────────────────────

def db_budget_vs_actual():
    fig = _new_fig("Budget vs Actual Analysis",
                   "Entity: ALL  |  Scenario: Actual vs Budget  |  Time: FY2025.Dec  |  View: YTD")

    # Grouped bar chart
    ax_bar = fig.add_axes([0.04, 0.48, 0.55, 0.4])
    categories = ["Revenue", "COGS", "Gross\nProfit", "OPEX", "EBITDA"]
    actual = [149.2, 98.9, 50.3, 28.0, 22.3]
    budget = [142.0, 94.5, 47.5, 27.0, 20.5]
    x = np.arange(len(categories))
    w = 0.3
    ax_bar.bar(x - w/2, actual, w, color=ACCENT_BLUE, label="Actual", edgecolor="none")
    ax_bar.bar(x + w/2, budget, w, color=MID_GRAY, label="Budget", edgecolor="none")
    for i in range(len(categories)):
        var_pct = (actual[i] / budget[i] - 1) * 100
        c = GREEN if var_pct >= 0 else RED
        if categories[i] == "COGS":
            c = RED if var_pct > 0 else GREEN
        ax_bar.text(i, max(actual[i], budget[i]) + 1, f"{var_pct:+.1f}%",
                    ha="center", fontsize=8.5, fontweight="bold", color=c)
    ax_bar.set_xticks(x)
    ax_bar.set_xticklabels(categories)
    ax_bar.set_title("Actual vs Budget ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_bar.legend(fontsize=8)
    ax_bar.set_facecolor(WHITE)
    ax_bar.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_bar.tick_params(labelsize=9, colors=DARK_GRAY)

    # Variance summary (right side)
    ax_var = fig.add_axes([0.64, 0.48, 0.34, 0.4])
    ax_var.axis("off"); ax_var.set_facecolor(WHITE)
    ax_var.set_title("Variance Summary ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Metric", "Actual", "Budget", "Var $", "Var %"]
    data = [
        ["Revenue", "149.2", "142.0", "+7.2", "+5.1%"],
        ["COGS", "98.9", "94.5", "+4.4", "+4.7%"],
        ["Gross Profit", "50.3", "47.5", "+2.8", "+5.9%"],
        ["OPEX", "28.0", "27.0", "+1.0", "+3.7%"],
        ["EBITDA", "22.3", "20.5", "+1.8", "+8.8%"],
        ["Net Income", "14.9", "13.5", "+1.4", "+10.4%"],
    ]
    tbl = ax_var.table(cellText=data, colLabels=cols, loc="center", cellLoc="center", colColours=[NAVY]*5)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8); tbl.scale(1, 1.5)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
        else:
            cell.set_facecolor(WHITE)

    # Heatmap table
    ax_hm = fig.add_axes([0.04, 0.04, 0.92, 0.38])
    ax_hm.axis("off"); ax_hm.set_facecolor(WHITE)
    ax_hm.set_title("Entity x Metric Variance Heatmap (%)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    entities = ["US01", "US02", "DE01", "UK01", "CN01", "JP01", "FR01", "AU01"]
    metrics = ["Revenue", "Gross Margin", "EBITDA", "Net Income", "Cash Flow"]
    np.random.seed(42)
    hm_data = np.random.uniform(-8, 12, (len(entities), len(metrics)))
    cell_text = [[f"{v:+.1f}%" for v in row] for row in hm_data]
    cell_colors = []
    for row in hm_data:
        rc = []
        for v in row:
            if v > 3: rc.append("#C8F7C5")
            elif v > 0: rc.append("#E8F5E9")
            elif v > -3: rc.append("#FFF9C4")
            else: rc.append("#FFCDD2")
        cell_colors.append(rc)
    tbl2 = ax_hm.table(cellText=cell_text, rowLabels=entities, colLabels=metrics,
                        loc="center", cellLoc="center", cellColours=cell_colors,
                        colColours=[NAVY]*5, rowColours=[LIGHT_GRAY]*8)
    tbl2.auto_set_font_size(False); tbl2.set_fontsize(8); tbl2.scale(1, 1.35)
    for (r, c), cell in tbl2.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")

    _save(fig, "DB_BudgetVsActual.png")


# ── 9. DB_RollingForecastTrend ─────────────────────────────────────────────

def db_rolling_forecast_trend():
    fig = _new_fig("Rolling Forecast Trend",
                   "Entity: ALL  |  Scenario: RF Current / RF Prior / Budget / Actual  |  Time: 18-Month Window")

    ax = fig.add_axes([0.06, 0.12, 0.88, 0.72])
    months = ["Jul24","Aug24","Sep24","Oct24","Nov24","Dec24",
              "Jan25","Feb25","Mar25","Apr25","May25","Jun25",
              "Jul25","Aug25","Sep25","Oct25","Nov25","Dec25"]
    actual = [10.5,10.8,11.2,11.0,11.5,12.0, 12.3,12.5,12.8,13.0,13.2,13.5, None,None,None,None,None,None]
    budget = [10.0]*6 + [11.5]*6 + [12.0]*6
    rf_curr = [None]*6 + [None]*6 + [13.8,14.0,14.2,14.5,14.8,15.2]
    rf_prior = [None]*6 + [None]*6 + [13.5,13.7,13.8,14.0,14.2,14.5]

    # Confidence band
    rf_low = [None]*12 + [13.2,13.3,13.4,13.5,13.5,13.8]
    rf_high = [None]*12 + [14.4,14.7,15.0,15.5,16.1,16.6]

    x = list(range(len(months)))

    # Shade forecast region
    ax.axvspan(11.5, 17.5, color="#F0F4F8", zorder=0)
    ax.axvline(11.5, color=MID_GRAY, linewidth=1, linestyle=":", zorder=1)
    ax.text(11.7, 16.5, "Forecast", fontsize=9, color=MID_GRAY, fontstyle="italic")
    ax.text(9, 16.5, "Actual", fontsize=9, color=MID_GRAY, fontstyle="italic")

    # Confidence band
    x_fc = [i for i in range(12, 18)]
    ax.fill_between(x_fc, [rf_low[i] for i in x_fc], [rf_high[i] for i in x_fc],
                    color=GREEN, alpha=0.1, label="Confidence Band")

    # Budget (dashed gray, full)
    ax.plot(x, budget, color=MID_GRAY, linewidth=1.8, linestyle="--", label="Budget", zorder=2)

    # Actual (solid blue)
    act_x = [i for i in range(12)]
    act_y = [actual[i] for i in act_x]
    ax.plot(act_x, act_y, color=ACCENT_BLUE, linewidth=2.5, marker="o", markersize=4, label="Actual", zorder=3)

    # RF Current
    ax.plot(x_fc, [rf_curr[i] for i in x_fc], color=GREEN, linewidth=2.5, marker="o", markersize=4,
            label="RF Current", zorder=3)
    # RF Prior
    ax.plot(x_fc, [rf_prior[i] for i in x_fc], color=GREEN, linewidth=1.5, linestyle="--",
            marker="s", markersize=3, label="RF Prior", zorder=3, alpha=0.7)

    ax.set_xticks(x)
    ax.set_xticklabels(months, rotation=45, fontsize=7.5)
    ax.set_title("Revenue: Actual, Budget & Rolling Forecast ($M)", fontsize=13, fontweight="bold", color=NAVY, loc="left")
    ax.legend(fontsize=8.5, loc="upper left", ncol=5)
    ax.set_facecolor(WHITE)
    ax.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax.tick_params(labelsize=8, colors=DARK_GRAY)
    ax.set_ylim(8, 17)
    ax.set_ylabel("Revenue ($M)", fontsize=10, color=NAVY)

    _save(fig, "DB_RollingForecastTrend.png")


# ── 10. DB_PeoplePlanning ─────────────────────────────────────────────────

def db_people_planning():
    fig = _new_fig("People Planning Dashboard",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD")

    # KPI tiles
    kpis = [
        ("Total FTE", "1,842", "+48 YoY", GREEN),
        ("Avg Compensation", "$78,500", "+3.2%", ORANGE),
        ("Total People Cost", "$144.6M", "+5.1%", ORANGE),
        ("Turnover Rate", "12.3%", "-1.5pp", GREEN),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpis):
        ax = fig.add_axes([0.02 + i * 0.245, 0.78, 0.23, 0.1], facecolor=WHITE)
        _kpi_tile(ax, lbl, val, delta, dc)

    # Stacked bar: Headcount by function across entities
    ax_bar = fig.add_axes([0.04, 0.38, 0.55, 0.36])
    entities = ["US01", "US02", "DE01", "UK01", "CN01", "JP01"]
    production = [220, 180, 200, 140, 280, 120]
    engineering = [80, 60, 90, 50, 60, 70]
    sales = [60, 50, 40, 45, 30, 35]
    admin = [40, 35, 30, 25, 20, 15]
    rnd = [50, 25, 60, 20, 15, 45]
    ax_bar.bar(entities, production, color=ACCENT_BLUE, label="Production", width=0.5)
    ax_bar.bar(entities, engineering, bottom=production, color=TEAL, label="Engineering", width=0.5)
    b2 = [a+b for a,b in zip(production, engineering)]
    ax_bar.bar(entities, sales, bottom=b2, color=GREEN, label="Sales", width=0.5)
    b3 = [a+b for a,b in zip(b2, sales)]
    ax_bar.bar(entities, admin, bottom=b3, color=GOLD, label="Admin", width=0.5)
    b4 = [a+b for a,b in zip(b3, admin)]
    ax_bar.bar(entities, rnd, bottom=b4, color=ORANGE, label="R&D", width=0.5)
    ax_bar.set_title("Headcount by Function & Entity", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_bar.legend(fontsize=7, loc="upper right", ncol=2)
    ax_bar.set_facecolor(WHITE)
    ax_bar.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_bar.tick_params(labelsize=9, colors=DARK_GRAY)

    # Pie chart: Compensation breakdown
    ax_pie = fig.add_axes([0.65, 0.38, 0.32, 0.36])
    sizes = [58, 18, 12, 8, 4]
    labels = ["Base Salary\n58%", "Benefits\n18%", "Bonus\n12%", "Overtime\n8%", "Other\n4%"]
    pie_colors = [ACCENT_BLUE, TEAL, GREEN, GOLD, MID_GRAY]
    ax_pie.pie(sizes, labels=labels, colors=pie_colors, startangle=90,
               textprops={"fontsize": 8, "color": NAVY}, wedgeprops={"edgecolor": WHITE, "linewidth": 1.5})
    ax_pie.set_title("Compensation Breakdown", fontsize=11, fontweight="bold", color=NAVY)

    # Bottom: turnover trend
    ax_tr = fig.add_axes([0.04, 0.04, 0.92, 0.29])
    months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    hc = [1794,1802,1810,1818,1825,1830,1832,1835,1838,1840,1841,1842]
    ax_tr.bar(months, hc, color=ACCENT_BLUE, width=0.5, alpha=0.7)
    ax_tr2 = ax_tr.twinx()
    turnover = [14.2,13.8,13.5,13.2,13.0,12.8,12.6,12.5,12.4,12.3,12.3,12.3]
    ax_tr2.plot(months, turnover, color=RED, linewidth=2, marker="o", markersize=4)
    ax_tr.set_title("Headcount & Turnover Trend", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_tr.set_ylabel("Headcount", fontsize=9, color=ACCENT_BLUE)
    ax_tr2.set_ylabel("Turnover %", fontsize=9, color=RED)
    ax_tr.set_facecolor(WHITE)
    ax_tr.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_tr.tick_params(labelsize=8, colors=DARK_GRAY)
    ax_tr2.tick_params(labelsize=8, colors=RED)

    _save(fig, "DB_PeoplePlanning.png")


# ── 11. DB_CAPEXTracker ───────────────────────────────────────────────────

def db_capex_tracker():
    fig = _new_fig("CAPEX Project Tracker",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD")

    # KPI tiles
    kpis = [
        ("Total Budget", "$19.4M", "", None),
        ("Spent to Date", "$8.2M", "42.3%", ACCENT_BLUE),
        ("Remaining", "$11.2M", "", None),
        ("% On Track", "85%", "6 of 7", GREEN),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpis):
        ax = fig.add_axes([0.02 + i * 0.245, 0.78, 0.23, 0.1], facecolor=WHITE)
        _kpi_tile(ax, lbl, val, delta if delta else None, dc)

    # Horizontal bar: project status
    ax_bar = fig.add_axes([0.06, 0.18, 0.88, 0.56])
    projects = ["New Assembly Line", "Robotic Welding Cell", "AGV Fleet Deployment",
                "Warehouse Expansion", "ERP Integration", "Quality Lab Upgrade", "Solar Panel Install"]
    pct_complete = [72, 45, 25, 55, 88, 60, 15]
    budgets = [5.2, 3.8, 2.5, 4.1, 1.5, 1.2, 1.1]
    spent = [3.7, 1.7, 0.6, 2.3, 1.3, 0.7, 0.2]

    y = np.arange(len(projects))
    # Background (total=100%)
    ax_bar.barh(y, [100]*len(projects), height=0.5, color="#E8EEF4", edgecolor="none")
    # Progress
    bar_colors = [GREEN if p >= 50 else (GOLD if p >= 25 else MID_GRAY) for p in pct_complete]
    ax_bar.barh(y, pct_complete, height=0.5, color=bar_colors, edgecolor="none")
    for i, (p, b, s) in enumerate(zip(pct_complete, budgets, spent)):
        ax_bar.text(p + 1.5, i, f"{p}%  (${s:.1f}M / ${b:.1f}M)", va="center", fontsize=8.5,
                    fontweight="bold", color=NAVY)
    ax_bar.set_yticks(y)
    ax_bar.set_yticklabels(projects, fontsize=10, color=NAVY, fontweight="bold")
    ax_bar.set_xlim(0, 110)
    ax_bar.set_title("Project Completion Status", fontsize=12, fontweight="bold", color=NAVY, loc="left")
    ax_bar.set_facecolor(WHITE)
    ax_bar.set_xlabel("% Complete", fontsize=9, color=DARK_GRAY)
    ax_bar.tick_params(labelsize=8, colors=DARK_GRAY)
    for spine in ["top", "right"]:
        ax_bar.spines[spine].set_visible(False)

    # Monthly spend trend
    ax_tr = fig.add_axes([0.06, 0.04, 0.88, 0.11])
    months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    monthly_spend = [0.5, 0.6, 0.7, 0.8, 0.9, 0.7, 0.8, 0.6, 0.7, 0.5, 0.5, 0.9]
    cum_spend = np.cumsum(monthly_spend)
    ax_tr.bar(months, monthly_spend, color=ACCENT_BLUE, width=0.5, alpha=0.7)
    ax_tr3 = ax_tr.twinx()
    ax_tr3.plot(months, cum_spend, color=RED, linewidth=2, marker="o", markersize=3)
    ax_tr.set_facecolor(WHITE); ax_tr.tick_params(labelsize=7, colors=DARK_GRAY)
    ax_tr3.tick_params(labelsize=7, colors=RED)
    ax_tr.set_ylabel("Monthly ($M)", fontsize=7, color=ACCENT_BLUE)
    ax_tr3.set_ylabel("Cumul. ($M)", fontsize=7, color=RED)

    _save(fig, "DB_CAPEXTracker.png")


# ── 12. DB_IntercompanyRecon ───────────────────────────────────────────────

def db_intercompany_recon():
    fig = _new_fig("Intercompany Reconciliation",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: Period")

    # Summary KPIs
    kpis = [
        ("Total IC Pairs", "48", None, None),
        ("Matched", "42", "87.5%", GREEN),
        ("Unmatched", "6", "12.5%", RED),
        ("Tolerance", "$1,000", None, None),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpis):
        ax = fig.add_axes([0.02 + i * 0.245, 0.78, 0.23, 0.1], facecolor=WHITE)
        _kpi_tile(ax, lbl, val, delta, dc)

    # Unmatched table
    ax_tbl = fig.add_axes([0.04, 0.42, 0.92, 0.33])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Unmatched Intercompany Items (> $1,000 Tolerance)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Entity A", "Entity B", "Amount A ($)", "Amount B ($)", "Difference ($)", "Status"]
    data = [
        ["US01", "DE01", "1,250,000", "1,247,500", "2,500", "UNMATCHED"],
        ["US02", "UK01", "875,300", "871,100", "4,200", "UNMATCHED"],
        ["DE01", "CN01", "540,000", "538,200", "1,800", "UNMATCHED"],
        ["UK01", "JP01", "320,000", "317,500", "2,500", "UNMATCHED"],
        ["CN01", "FR01", "215,000", "213,800", "1,200", "UNMATCHED"],
        ["JP01", "AU01", "180,000", "178,100", "1,900", "UNMATCHED"],
    ]
    cell_colors = [[WHITE]*5 + ["#FFCDD2"] for _ in data]
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center",
                       cellColours=cell_colors, colColours=[NAVY]*6)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8.5); tbl.scale(1, 1.5)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")
        if c == 5 and r > 0:
            cell.set_text_props(color=RED, fontweight="bold")

    # IC balance bar chart
    ax_bar = fig.add_axes([0.04, 0.04, 0.92, 0.34])
    pairs = ["US01-DE01", "US02-UK01", "DE01-CN01", "UK01-JP01", "CN01-FR01", "JP01-AU01",
             "US01-CN01", "DE01-FR01"]
    balances = [2500, 4200, 1800, 2500, 1200, 1900, 500, 300]
    colors_b = [RED if b > 1000 else GREEN for b in balances]
    ax_bar.bar(pairs, balances, color=colors_b, width=0.55, edgecolor="none")
    ax_bar.axhline(1000, color=ORANGE, linewidth=1.5, linestyle="--", label="Tolerance $1,000")
    ax_bar.set_title("IC Balance Difference by Entity Pair ($)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_bar.legend(fontsize=8)
    ax_bar.set_facecolor(WHITE)
    ax_bar.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_bar.tick_params(labelsize=8, colors=DARK_GRAY, rotation=30)

    _save(fig, "DB_IntercompanyRecon.png")


# ── 13. DB_DataQualityScorecard ────────────────────────────────────────────

def db_data_quality_scorecard():
    fig = _new_fig("Data Quality Scorecard",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: Period")

    # Large donut gauge - Overall DQ Score
    ax_gauge = fig.add_axes([0.04, 0.42, 0.3, 0.46])
    score = 94.2
    ax_gauge.pie([score, 100-score], colors=[GREEN, "#E8EEF4"], startangle=90,
                 wedgeprops={"width": 0.3, "edgecolor": WHITE, "linewidth": 2})
    ax_gauge.text(0, 0, f"{score}%", ha="center", va="center", fontsize=28, fontweight="bold", color=NAVY)
    ax_gauge.text(0, -0.2, "Overall DQ Score", ha="center", va="center", fontsize=10, color=MID_GRAY, fontweight="bold")

    # Individual score donuts
    sub_scores = [
        ("Completeness", 97, GREEN),
        ("Accuracy", 93, ACCENT_BLUE),
        ("Timeliness", 95, TEAL),
        ("Consistency", 91, GOLD),
    ]
    for i, (lbl, sc, clr) in enumerate(sub_scores):
        ax = fig.add_axes([0.36 + i * 0.16, 0.55, 0.14, 0.32])
        ax.pie([sc, 100-sc], colors=[clr, "#E8EEF4"], startangle=90,
               wedgeprops={"width": 0.35, "edgecolor": WHITE, "linewidth": 1.5})
        ax.text(0, 0.05, f"{sc}%", ha="center", va="center", fontsize=16, fontweight="bold", color=NAVY)
        ax.text(0, -0.25, lbl, ha="center", va="center", fontsize=8, color=MID_GRAY, fontweight="bold")

    # Validation rules table
    ax_tbl = fig.add_axes([0.04, 0.04, 0.92, 0.42])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Validation Rule Results", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Rule Name", "Category", "Pass Count", "Fail Count", "% Pass", "Trend"]
    data = [
        ["BS Balance Check", "Accuracy", "312", "0", "100.0%", "\u25B2"],
        ["IC Elimination Check", "Accuracy", "48", "2", "96.0%", "\u25BC"],
        ["Account Completeness", "Completeness", "1,842", "56", "97.0%", "\u25B2"],
        ["FX Rate Validation", "Accuracy", "24", "1", "96.0%", "\u25B2"],
        ["Data Load Timeliness", "Timeliness", "12", "1", "92.3%", "\u2014"],
        ["Journal Entry Approval", "Consistency", "245", "12", "95.1%", "\u25B2"],
        ["Variance Threshold", "Accuracy", "156", "18", "89.7%", "\u25BC"],
        ["Segment Mapping", "Completeness", "890", "5", "99.4%", "\u25B2"],
        ["Currency Code Valid", "Accuracy", "1,200", "0", "100.0%", "\u25B2"],
        ["Period Lock Status", "Timeliness", "12", "0", "100.0%", "\u2014"],
    ]
    pass_colors = []
    for r in data:
        rc = [WHITE] * 5
        pct = float(r[4].replace("%",""))
        if pct >= 99: rc[4] = "#C8F7C5"
        elif pct >= 95: rc[4] = "#E8F5E9"
        elif pct >= 90: rc[4] = "#FFF9C4"
        else: rc[4] = "#FFCDD2"
        # Trend color
        if "\u25B2" in r[5]: rc.append("#C8F7C5")
        elif "\u25BC" in r[5]: rc.append("#FFCDD2")
        else: rc.append(WHITE)
        pass_colors.append(rc)
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center",
                       cellColours=pass_colors, colColours=[NAVY]*6)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8); tbl.scale(1, 1.25)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")

    _save(fig, "DB_DataQualityScorecard.png")


# ── 14. DB_KPICockpit ─────────────────────────────────────────────────────

def db_kpi_cockpit():
    fig = _new_fig("KPI Cockpit",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD")

    kpis = [
        ("Gross Margin", "37.9%", "+1.2pp", GREEN),
        ("EBITDA Margin", "18.6%", "+0.8pp", GREEN),
        ("ROIC", "12.8%", "+0.6pp", GREEN),
        ("Asset Turnover", "1.8x", "+0.1x", GREEN),
        ("DSO", "42 days", "-3 days", GREEN),
        ("DPO", "38 days", "+2 days", GREEN),
        ("DIO", "55 days", "-4 days", GREEN),
        ("Revenue / FTE", "$81K", "+$3K", GREEN),
        ("Cost / Unit", "$42.50", "-$1.20", GREEN),
        ("OEE", "82.1%", "+2.3pp", GREEN),
        ("Capacity Util.", "84.4%", "+1.1pp", GREEN),
        ("Working Capital", "$59.2M", "+$3.1M", ORANGE),
    ]

    np.random.seed(123)
    for idx, (lbl, val, delta, dc) in enumerate(kpis):
        row = idx // 4
        col = idx % 4
        ax = fig.add_axes([0.015 + col * 0.248, 0.6 - row * 0.3, 0.235, 0.27], facecolor=WHITE)
        for spine in ax.spines.values():
            spine.set_edgecolor("#D0D8E0"); spine.set_linewidth(0.8)
        ax.set_xticks([]); ax.set_yticks([])
        ax.set_xlim(0, 1); ax.set_ylim(0, 1)

        ax.text(0.5, 0.88, lbl, color=MID_GRAY, fontsize=9, ha="center", va="center",
                fontfamily="sans-serif", fontweight="bold")
        ax.text(0.5, 0.58, str(val), color=NAVY, fontsize=22, ha="center", va="center",
                fontfamily="sans-serif", fontweight="bold")
        arrow = "\u25B2" if dc == GREEN else "\u25BC"
        ax.text(0.5, 0.35, f"{arrow} {delta}", color=dc, fontsize=10,
                ha="center", va="center", fontweight="bold")

        # Sparkline
        spark = np.cumsum(np.random.randn(12)) + 50
        ins = ax.inset_axes([0.1, 0.02, 0.8, 0.22])
        ins.plot(spark, color=ACCENT_BLUE, linewidth=1.2)
        ins.fill_between(range(len(spark)), spark, alpha=0.1, color=ACCENT_BLUE)
        ins.axis("off")

    _save(fig, "DB_KPICockpit.png")


# ── 15. DB_AccountReconStatus ─────────────────────────────────────────────

def db_account_recon_status():
    fig = _new_fig("Account Reconciliation Status",
                   "Entity: ALL  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: Period")

    # Progress bar
    ax_prog = fig.add_axes([0.04, 0.82, 0.92, 0.065], facecolor=WHITE)
    ax_prog.set_xlim(0, 100); ax_prog.set_ylim(0, 1)
    ax_prog.barh(0.5, 100, height=0.6, color="#E0E0E0", edgecolor="none", zorder=0)
    ax_prog.barh(0.5, 78.5, height=0.6, color=GREEN, edgecolor="none", zorder=1)
    ax_prog.text(50, 0.5, "245 of 312 Accounts Reconciled  \u2014  78.5% Complete",
                 ha="center", va="center", color=WHITE, fontsize=11, fontweight="bold", zorder=2)
    ax_prog.axis("off")

    # Risk breakdown: horizontal stacked bar
    ax_risk = fig.add_axes([0.04, 0.67, 0.92, 0.12])
    ax_risk.set_xlim(0, 312); ax_risk.set_ylim(0, 1)
    ax_risk.barh(0.5, 272, height=0.5, color=GREEN, edgecolor="none", label="Low Risk: 272")
    ax_risk.barh(0.5, 28, left=272, height=0.5, color=GOLD, edgecolor="none", label="Medium Risk: 28")
    ax_risk.barh(0.5, 12, left=300, height=0.5, color=RED, edgecolor="none", label="High Risk: 12")
    ax_risk.text(136, 0.5, "Low: 272", ha="center", va="center", fontsize=10, fontweight="bold", color=WHITE)
    ax_risk.text(286, 0.5, "Med: 28", ha="center", va="center", fontsize=9, fontweight="bold", color=WHITE)
    ax_risk.text(306, 0.5, "12", ha="center", va="center", fontsize=9, fontweight="bold", color=WHITE)
    ax_risk.axis("off")
    ax_risk.set_title("Risk Breakdown", fontsize=11, fontweight="bold", color=NAVY, loc="left")

    # Reconciliation by category bar chart
    ax_bar = fig.add_axes([0.04, 0.33, 0.44, 0.3])
    acct_cats = ["Cash", "AR", "AP", "Inventory", "Fixed\nAssets", "Accruals", "IC"]
    recon = [32, 45, 38, 28, 42, 35, 25]
    total = [35, 52, 42, 32, 48, 40, 30]
    unrecon = [t - r for t, r in zip(total, recon)]
    ax_bar.bar(acct_cats, recon, color=GREEN, label="Reconciled", width=0.5)
    ax_bar.bar(acct_cats, unrecon, bottom=recon, color=RED, label="Unreconciled", width=0.5, alpha=0.7)
    ax_bar.set_title("Reconciliation by Account Category", fontsize=10, fontweight="bold", color=NAVY, loc="left")
    ax_bar.legend(fontsize=7)
    ax_bar.set_facecolor(WHITE)
    ax_bar.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_bar.tick_params(labelsize=8, colors=DARK_GRAY)

    # Aging pie
    ax_pie = fig.add_axes([0.54, 0.33, 0.2, 0.3])
    ages = [45, 15, 5, 2]
    age_labels = ["<7d\n67%", "7-14d\n22%", "14-30d\n8%", ">30d\n3%"]
    age_colors = [GREEN, GOLD, ORANGE, RED]
    ax_pie.pie(ages, labels=age_labels, colors=age_colors, startangle=90,
               textprops={"fontsize": 7.5, "color": NAVY}, wedgeprops={"edgecolor": WHITE, "linewidth": 1})
    ax_pie.set_title("Aging", fontsize=10, fontweight="bold", color=NAVY)

    # Daily completion trend
    ax_tr = fig.add_axes([0.78, 0.33, 0.2, 0.3])
    days = list(range(1, 9))
    completed = [20, 55, 110, 155, 190, 220, 238, 245]
    ax_tr.plot(days, completed, color=ACCENT_BLUE, linewidth=2, marker="o", markersize=4)
    ax_tr.axhline(312, color=RED, linewidth=1, linestyle="--")
    ax_tr.set_title("Daily Progress", fontsize=10, fontweight="bold", color=NAVY, loc="left")
    ax_tr.set_xlabel("Close Day", fontsize=8)
    ax_tr.set_facecolor(WHITE)
    ax_tr.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_tr.tick_params(labelsize=7, colors=DARK_GRAY)

    # High-risk unreconciled accounts table
    ax_tbl = fig.add_axes([0.04, 0.03, 0.92, 0.27])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("High-Risk Unreconciled Accounts", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Account", "Description", "Balance ($)", "Variance ($)", "Age (Days)", "Owner", "Status"]
    data = [
        ["1100-100", "Cash - Operating", "4,250,000", "15,200", "5", "J. Smith", "In Progress"],
        ["1200-200", "AR - Trade", "8,120,000", "42,500", "8", "M. Johnson", "Pending"],
        ["2100-100", "AP - Trade", "5,890,000", "28,300", "12", "S. Williams", "Pending"],
        ["1300-100", "Inventory - Raw", "3,450,000", "18,700", "6", "R. Brown", "In Progress"],
        ["1500-200", "PP&E - Machinery", "12,500,000", "95,000", "15", "T. Davis", "Escalated"],
        ["2200-100", "Accrued Expenses", "2,100,000", "8,400", "4", "K. Wilson", "In Progress"],
    ]
    cell_colors = []
    for r in data:
        rc = [WHITE]*6
        age = int(r[4])
        if age > 10: rc.append("#FFCDD2")
        elif age > 7: rc.append("#FFF9C4")
        else: rc.append("#E8F5E9")
        cell_colors.append(rc)
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center",
                       cellColours=cell_colors, colColours=[NAVY]*7)
    tbl.auto_set_font_size(False); tbl.set_fontsize(7.5); tbl.scale(1, 1.35)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")

    _save(fig, "DB_AccountReconStatus.png")


# ── 16. DB_SupplyChainAnalytics ────────────────────────────────────────────

def db_supply_chain_analytics():
    fig = _new_fig("Supply Chain Analytics",
                   "Entity: MFG Plants  |  Scenario: Actual  |  Time: FY2025.Dec  |  View: YTD")

    # KPI tiles
    kpis = [
        ("Inventory Turns", "6.2x", "+0.4x", GREEN),
        ("Days of Supply", "58", "-3 days", GREEN),
        ("Fill Rate", "97.2%", "+0.8pp", GREEN),
        ("On-Time Delivery", "94.8%", "+1.2pp", GREEN),
    ]
    for i, (lbl, val, delta, dc) in enumerate(kpis):
        ax = fig.add_axes([0.02 + i * 0.245, 0.78, 0.23, 0.1], facecolor=WHITE)
        _kpi_tile(ax, lbl, val, delta, dc)

    # Stacked area: Inventory breakdown over time
    ax_area = fig.add_axes([0.04, 0.4, 0.55, 0.34])
    months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    raw = [12.5,12.8,13.0,12.5,12.2,12.0,11.8,12.1,12.3,12.0,11.8,11.5]
    wip = [8.2,8.5,8.8,8.5,8.3,8.0,7.8,8.0,8.2,8.0,7.8,7.5]
    fg = [15.0,14.8,15.2,14.5,14.0,13.8,14.2,14.5,14.0,13.5,13.2,13.0]
    ax_area.stackplot(months, raw, wip, fg, labels=["Raw Materials", "WIP", "Finished Goods"],
                      colors=[ACCENT_BLUE, TEAL, GREEN], alpha=0.7)
    ax_area.set_title("Inventory Breakdown Over Time ($M)", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    ax_area.legend(fontsize=7, loc="upper right")
    ax_area.set_facecolor(WHITE)
    ax_area.grid(axis="y", color="#E0E0E0", linewidth=0.5)
    ax_area.tick_params(labelsize=8, colors=DARK_GRAY)

    # Supplier performance horizontal bar
    ax_supp = fig.add_axes([0.64, 0.4, 0.34, 0.34])
    suppliers = ["Supplier A", "Supplier B", "Supplier C", "Supplier D", "Supplier E",
                 "Supplier F", "Supplier G", "Supplier H"]
    perf = [98.5, 97.2, 96.8, 95.5, 94.2, 93.0, 91.5, 88.0]
    colors_s = [GREEN if p >= 95 else (GOLD if p >= 90 else RED) for p in perf]
    y = np.arange(len(suppliers))
    ax_supp.barh(y, perf, color=colors_s, height=0.5, edgecolor="none")
    ax_supp.set_yticks(y); ax_supp.set_yticklabels(suppliers, fontsize=8)
    ax_supp.set_xlim(80, 100)
    ax_supp.axvline(95, color=RED, linewidth=1, linestyle="--")
    for i, v in enumerate(perf):
        ax_supp.text(v + 0.2, i, f"{v}%", va="center", fontsize=7.5, fontweight="bold", color=NAVY)
    ax_supp.set_title("Supplier Performance (%)", fontsize=10, fontweight="bold", color=NAVY, loc="left")
    ax_supp.set_facecolor(WHITE)
    ax_supp.tick_params(labelsize=8, colors=DARK_GRAY)

    # Bottom: logistics metrics table
    ax_tbl = fig.add_axes([0.04, 0.04, 0.92, 0.32])
    ax_tbl.axis("off"); ax_tbl.set_facecolor(WHITE)
    ax_tbl.set_title("Supply Chain Metrics by Plant", fontsize=11, fontweight="bold", color=NAVY, loc="left")
    cols = ["Plant", "Inv. Turns", "Days Supply", "Fill Rate %", "OTD %", "Backorder $K", "Trend"]
    data = [
        ["US01", "6.5x", "55", "97.8%", "95.2%", "120", "\u25B2"],
        ["US02", "5.8x", "62", "96.5%", "94.1%", "185", "\u25BC"],
        ["DE01", "6.8x", "53", "98.2%", "96.0%", "85", "\u25B2"],
        ["UK01", "6.0x", "60", "97.0%", "94.5%", "145", "\u2014"],
        ["CN01", "5.5x", "65", "96.0%", "93.2%", "220", "\u25BC"],
        ["JP01", "7.2x", "50", "98.5%", "97.1%", "45", "\u25B2"],
    ]
    cell_colors = []
    for r in data:
        rc = [WHITE] * 6
        if "\u25B2" in r[6]: rc.append("#C8F7C5")
        elif "\u25BC" in r[6]: rc.append("#FFCDD2")
        else: rc.append(WHITE)
        cell_colors.append(rc)
    tbl = ax_tbl.table(cellText=data, colLabels=cols, loc="center", cellLoc="center",
                       cellColours=cell_colors, colColours=[NAVY]*7)
    tbl.auto_set_font_size(False); tbl.set_fontsize(8.5); tbl.scale(1, 1.45)
    for (r, c), cell in tbl.get_celld().items():
        cell.set_edgecolor("#D0D8E0")
        if r == 0:
            cell.set_text_props(color=WHITE, fontweight="bold")

    _save(fig, "DB_SupplyChainAnalytics.png")


# ── Main ───────────────────────────────────────────────────────────────────

def main():
    print("Generating OneStream Dashboard Mockups...")
    print(f"Output directory: {OUTPUT_DIR}\n")

    generators = [
        db_executive_summary,
        db_consolidation_status,
        db_plant_performance,
        db_production_variance,
        db_pl_waterfall,
        db_balance_sheet,
        db_cash_flow,
        db_budget_vs_actual,
        db_rolling_forecast_trend,
        db_people_planning,
        db_capex_tracker,
        db_intercompany_recon,
        db_data_quality_scorecard,
        db_kpi_cockpit,
        db_account_recon_status,
        db_supply_chain_analytics,
    ]

    for i, gen in enumerate(generators, 1):
        print(f"[{i:2d}/16] {gen.__name__}...")
        gen()

    print(f"\nAll 16 dashboard mockups generated in {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
