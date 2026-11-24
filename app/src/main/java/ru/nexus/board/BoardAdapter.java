package ru.nexus.board;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;

public class BoardAdapter extends RecyclerView.Adapter<RecyclerView.ViewHolder> {

    static class Section {
        final String name;
        final int count;

        Section(String name, int count) {
            this.name = name;
            this.count = count;
        }
    }

    static class Task {
        String title;
        String note;
        String chip;
        int progress;
        String id;
    }

    private static final int T_SECTION = 0;
    private static final int T_TASK = 1;
    private final List<Object> items = new ArrayList<>();

    public void setItems(List<Object> next) {
        items.clear();
        items.addAll(next);
        notifyDataSetChanged();
    }

    @Override
    public int getItemViewType(int position) {
        return items.get(position) instanceof Section ? T_SECTION : T_TASK;
    }

    @Override
    public int getItemCount() {
        return items.size();
    }

    @NonNull
    @Override
    public RecyclerView.ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        LayoutInflater inf = LayoutInflater.from(parent.getContext());
        if (viewType == T_SECTION) {
            return new SectionHolder(inf.inflate(R.layout.item_section, parent, false));
        }
        return new TaskHolder(inf.inflate(R.layout.item_task, parent, false));
    }

    @Override
    public void onBindViewHolder(@NonNull RecyclerView.ViewHolder holder, int position) {
        Object o = items.get(position);
        if (holder instanceof SectionHolder && o instanceof Section) {
            SectionHolder h = (SectionHolder) holder;
            Section s = (Section) o;
            h.name.setText(s.name);
            h.count.setText(String.valueOf(s.count));
        } else if (holder instanceof TaskHolder && o instanceof Task) {
            TaskHolder h = (TaskHolder) holder;
            Task t = (Task) o;
            h.title.setText(t.title);
            h.chip.setText(t.chip);
            h.chip.setVisibility(t.chip == null || t.chip.isEmpty() ? View.GONE : View.VISIBLE);
            if (t.note == null || t.note.trim().isEmpty()) {
                h.note.setVisibility(View.GONE);
            } else {
                h.note.setVisibility(View.VISIBLE);
                h.note.setText(t.note.trim());
            }
            h.bar.setProgress(Math.max(0, Math.min(100, t.progress)));
            h.pct.setText(t.progress + "%");
        }
    }

    static class SectionHolder extends RecyclerView.ViewHolder {
        final TextView name;
        final TextView count;

        SectionHolder(View v) {
            super(v);
            name = v.findViewById(R.id.sec_name);
            count = v.findViewById(R.id.sec_count);
        }
    }

    static class TaskHolder extends RecyclerView.ViewHolder {
        final TextView title;
        final TextView chip;
        final TextView note;
        final TextView pct;
        final ProgressBar bar;

        TaskHolder(View v) {
            super(v);
            title = v.findViewById(R.id.task_title);
            chip = v.findViewById(R.id.task_chip);
            note = v.findViewById(R.id.task_note);
            pct = v.findViewById(R.id.task_pct);
            bar = v.findViewById(R.id.task_bar);
        }
    }
}
