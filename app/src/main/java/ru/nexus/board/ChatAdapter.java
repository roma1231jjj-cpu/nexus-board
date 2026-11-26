package ru.nexus.board;

import android.content.Context;
import android.view.Gravity;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class ChatAdapter extends RecyclerView.Adapter<ChatAdapter.MsgHolder> {

    static class Msg {
        String id;
        String text;
        long ts;
        boolean owner;
        boolean pending;
    }

    private final List<Msg> items = new ArrayList<>();
    private final SimpleDateFormat time = new SimpleDateFormat("HH:mm", Locale.getDefault());
    private int maxWidth = 0;

    public void add(Msg m) {
        items.add(m);
        notifyItemInserted(items.size() - 1);
    }

    public void remove(Msg m) {
        int i = items.indexOf(m);
        if (i < 0) return;
        items.remove(i);
        notifyItemRemoved(i);
    }

    public void touch() {
        notifyDataSetChanged();
    }

    /** Ширина пузыря зависит от ширины экрана: пересчитать при смене ориентации. */
    public void resetMaxWidth() {
        maxWidth = 0;
        notifyDataSetChanged();
    }

    /** Оптимистичное сообщение, которое сервер уже подтвердил (тот же текст и ts >=). */
    public Msg takePending(String text, long ts) {
        for (int i = items.size() - 1; i >= 0; i--) {
            Msg m = items.get(i);
            if (!m.pending || !m.owner) continue;
            if (m.text == null || !m.text.equals(text)) continue;
            if (m.ts <= ts + 120000L) return m;
        }
        return null;
    }

    @Override
    public int getItemCount() {
        return items.size();
    }

    @NonNull
    @Override
    public MsgHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        if (maxWidth == 0) {
            maxWidth = (int) (parent.getResources().getDisplayMetrics().widthPixels * 0.72f);
        }
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_chat, parent, false);
        return new MsgHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull MsgHolder h, int position) {
        Msg m = items.get(position);
        Context c = h.itemView.getContext();
        h.row.setGravity(m.owner ? Gravity.END : Gravity.START);
        h.bubble.setBackgroundResource(m.owner ? R.drawable.nx_bubble_owner : R.drawable.nx_bubble_ai);
        h.bubble.setAlpha(m.pending ? 0.6f : 1f);
        if (maxWidth == 0) {
            maxWidth = (int) (c.getResources().getDisplayMetrics().widthPixels * 0.72f);
        }
        h.text.setMaxWidth(maxWidth);
        h.text.setText(m.text);
        h.text.setTextColor(NxBackdrop.color(c, m.owner ? R.attr.nxOnPrimary : R.attr.nxText));
        h.time.setText(time.format(new Date(m.ts > 0 ? m.ts : System.currentTimeMillis())));
        h.time.setTextColor(NxBackdrop.color(c, m.owner ? R.attr.nxOnPrimary : R.attr.nxTextDim));
        h.time.setAlpha(m.owner ? 0.7f : 1f);
    }

    static class MsgHolder extends RecyclerView.ViewHolder {
        final LinearLayout row;
        final LinearLayout bubble;
        final TextView text;
        final TextView time;

        MsgHolder(View v) {
            super(v);
            row = v.findViewById(R.id.chat_row);
            bubble = v.findViewById(R.id.chat_bubble);
            text = v.findViewById(R.id.chat_text);
            time = v.findViewById(R.id.chat_time);
        }
    }
}
