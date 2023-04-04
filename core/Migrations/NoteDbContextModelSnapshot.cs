﻿// <auto-generated />
using System;
using MemoriaNote;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace MemoriaNote.Core.Migrations
{
    [DbContext(typeof(NoteDbContext))]
    partial class NoteDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.15");

            modelBuilder.Entity("MemoriaNote.Content", b =>
                {
                    b.Property<string>("Uuid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ContentType")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreateTime")
                        .HasColumnType("TEXT");

                    b.Property<int>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsErased")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<int>("Rowid")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Tags")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdateTime")
                        .HasColumnType("TEXT");

                    b.HasKey("Uuid");

                    b.HasIndex("Name", "Index");

                    b.ToTable("Contents");
                });

            modelBuilder.Entity("MemoriaNote.NoteKeyValue", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("TEXT");

                    b.Property<string>("Value")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("Metadata");
                });

            modelBuilder.Entity("MemoriaNote.Page", b =>
                {
                    b.Property<int>("Rowid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ContentType")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreateTime")
                        .HasColumnType("TEXT");

                    b.Property<int>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsErased")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<string>("Tags")
                        .HasColumnType("TEXT");

                    b.Property<string>("Text")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdateTime")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uuid")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Rowid");

                    b.HasAlternateKey("Uuid");

                    b.HasIndex("Name", "Index");

                    b.ToTable("Pages");
                });
#pragma warning restore 612, 618
        }
    }
}
